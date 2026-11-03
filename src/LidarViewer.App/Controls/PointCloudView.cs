using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LidarViewer.Core.Models;

namespace LidarViewer.App.Controls;

/// <summary>
/// Płaski widok chmury punktów: rzut z góry (domyślnie XY: oś X w prawo, Y w górę, Z pomijane; można przełączyć na XZ i YZ).
///
/// Algorytm rysowania (przy każdej zmianie chmury, rozmiaru, zoomu lub przesunięcia):
///  1. Obliczamy skalę "dopasuj do okna" z prostokąta otaczającego chmurę (BoundsMin/BoundsMax)
///     i mnożymy ją przez zoom użytkownika.
///  2. Każdy punkt (x, y) przeliczamy na piksel: środek chmury trafia na środek widoku
///     (plus przesunięcie od przeciągania myszą), a oś Y odwracamy, bo piksele rosną w dół.
///  3. Piksele zapisujemy do tablicy int[] (format BGRA), a ta trafia do WriteableBitmap
///     rysowanej na całej kontrolce. Zapis do bufora jest dużo szybszy niż
///     setki tysięcy osobnych wywołań rysowania.
///
/// Sterowanie: przeciągnięcie myszą przesuwa widok, kółko myszy przybliża/oddala.
/// </summary>
public sealed class PointCloudView : Control
{
    /// <summary>Chmura do wyświetlenia (null = pusty widok).</summary>
    public static readonly StyledProperty<PointCloud?> PointCloudProperty =
        AvaloniaProperty.Register<PointCloudView, PointCloud?>(nameof(PointCloud));

    public PointCloud? PointCloud
    {
        get => GetValue(PointCloudProperty);
        set => SetValue(PointCloudProperty, value);
    }

    /// <summary>Płaszczyzna rzutu: XY (z góry), XZ lub YZ (z boku).</summary>
    public static readonly StyledProperty<ProjectionPlane> PlaneProperty =
        AvaloniaProperty.Register<PointCloudView, ProjectionPlane>(nameof(Plane), ProjectionPlane.XY);

    public ProjectionPlane Plane
    {
        get => GetValue(PlaneProperty);
        set => SetValue(PlaneProperty, value);
    }

    private const int Background = unchecked((int)0xFF1A1A1A); // ciemnoszary
    private const int PointColor = unchecked((int)0xFF33E64D);  // jednolity zielony (BGRA: 0xAARRGGBB)

    private float _zoom = 1f;
    private Vector _pan;           // przesunięcie widoku w pikselach fizycznych
    private Point? _lastPointer;
    private int[] _pixels = [];
    private WriteableBitmap? _bitmap;

    static PointCloudView()
    {
        // Nowa chmura => wróć do widoku "dopasuj do okna" i przerysuj.
        PointCloudProperty.Changed.AddClassHandler<PointCloudView>((view, _) => view.ResetView());
        // Zmiana płaszczyzny rzutu zmienia też zakres widoku, więc też wracamy do dopasowania do okna.
        PlaneProperty.Changed.AddClassHandler<PointCloudView>((view, _) => view.ResetView());
    }

    private void ResetView()
    {
        _zoom = 1f;
        _pan = default;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = (int)(Bounds.Width * scaling);
        int height = (int)(Bounds.Height * scaling);
        if (width <= 0 || height <= 0) return;

        // Przezroczysty prostokąt sprawia, że kontrolka odbiera zdarzenia myszy w całym obszarze.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        EnsureBuffers(width, height, scaling);
        Rasterize(width, height);
        CopyToBitmap(width, height);

        context.DrawImage(_bitmap!, new Rect(0, 0, width, height), new Rect(Bounds.Size));
    }

    /// <summary>Tworzy (lub odtwarza po zmianie rozmiaru) bufor pikseli i bitmapę.</summary>
    private void EnsureBuffers(int width, int height, double scaling)
    {
        if (_bitmap is not null && _bitmap.PixelSize.Width == width && _bitmap.PixelSize.Height == height)
            return;

        _bitmap?.Dispose();
        _pixels = new int[width * height];
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96 * scaling, 96 * scaling),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
    }

    /// <summary>Przelicza punkty chmury na piksele w buforze (kroki 1-2 algorytmu).</summary>
    private void Rasterize(int width, int height)
    {
        Array.Fill(_pixels, Background);
        if (PointCloud is not { Count: > 0 } cloud) return;

        // Osie rzutu: ha = oś pozioma, va = oś pionowa (indeksy 0=X, 1=Y, 2=Z w tablicy pozycji).
        var (ha, va) = Plane switch
        {
            ProjectionPlane.XZ => (0, 2),
            ProjectionPlane.YZ => (1, 2),
            _ => (0, 1),
        };

        float rangeX = Math.Max(Axis(cloud.BoundsMax, ha) - Axis(cloud.BoundsMin, ha), 1e-6f);
        float rangeY = Math.Max(Axis(cloud.BoundsMax, va) - Axis(cloud.BoundsMin, va), 1e-6f);
        // Skala dopasowująca całą chmurę do okna (z 5% marginesem) razy zoom użytkownika.
        float scale = Math.Min(width / rangeX, height / rangeY) * 0.95f * _zoom;

        float centerX = Axis(cloud.Center, ha), centerY = Axis(cloud.Center, va);
        float offsetX = width / 2f + (float)_pan.X;
        float offsetY = height / 2f + (float)_pan.Y;

        var p = cloud.Positions;
        for (int i = 0; i < p.Length; i += 3)
        {
            int x = (int)((p[i + ha] - centerX) * scale + offsetX);
            int y = (int)(offsetY - (p[i + va] - centerY) * scale); // oś pionowa odwrócona
            // Punkt rysujemy jako kwadrat 2x2, żeby był widoczny na ekranach Retina.
            for (int dy = 0; dy < 2; dy++)
            {
                int yy = y + dy;
                if ((uint)yy >= (uint)height) continue;
                for (int dx = 0; dx < 2; dx++)
                {
                    int xx = x + dx;
                    if ((uint)xx < (uint)width)
                        _pixels[yy * width + xx] = PointColor;
                }
            }
        }
    }

    /// <summary>Składowa wektora o indeksie 0=X, 1=Y, 2=Z.</summary>
    private static float Axis(System.Numerics.Vector3 v, int index) => index switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>Kopiuje bufor pikseli do bitmapy (krok 3), uwzględniając ewentualny stride.</summary>
    private void CopyToBitmap(int width, int height)
    {
        using var fb = _bitmap!.Lock();
        for (int row = 0; row < height; row++)
            Marshal.Copy(_pixels, row * width, fb.Address + row * fb.RowBytes, width);
    }

    // --- Sterowanie widokiem ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _lastPointer = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastPointer is not { } last) return;

        var p = e.GetPosition(this);
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _pan += (p - last) * scaling; // przesunięcie liczone w pikselach fizycznych
        _lastPointer = p;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.1f, (float)e.Delta.Y), 0.1f, 200f);
        InvalidateVisual();
    }
}
