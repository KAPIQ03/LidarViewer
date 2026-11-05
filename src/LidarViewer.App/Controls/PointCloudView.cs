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

public sealed class PointCloudView : Control
{
    public static readonly StyledProperty<PointCloud?> PointCloudProperty =
        AvaloniaProperty.Register<PointCloudView, PointCloud?>(nameof(PointCloud));

    public PointCloud? PointCloud
    {
        get => GetValue(PointCloudProperty);
        set => SetValue(PointCloudProperty, value);
    }

    public static readonly StyledProperty<ProjectionPlane> PlaneProperty =
        AvaloniaProperty.Register<PointCloudView, ProjectionPlane>(nameof(Plane), ProjectionPlane.XY);

    public ProjectionPlane Plane
    {
        get => GetValue(PlaneProperty);
        set => SetValue(PlaneProperty, value);
    }

    private const int Background = unchecked((int)0xFF1A1A1A);
    private const int PointColor = unchecked((int)0xFF33E64D);

    private float _zoom = 1f;
    private Vector _pan;
    private Point? _lastPointer;
    private int[] _pixels = [];
    private WriteableBitmap? _bitmap;

    static PointCloudView()
    {
        PointCloudProperty.Changed.AddClassHandler<PointCloudView>((view, _) => view.ResetView());
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

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        EnsureBuffers(width, height, scaling);
        Rasterize(width, height);
        CopyToBitmap(width, height);

        context.DrawImage(_bitmap!, new Rect(0, 0, width, height), new Rect(Bounds.Size));
    }

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

    private void Rasterize(int width, int height)
    {
        Array.Fill(_pixels, Background);
        if (PointCloud is not { Count: > 0 } cloud) return;

        var (ha, va) = Plane switch
        {
            ProjectionPlane.XZ => (0, 2),
            ProjectionPlane.YZ => (1, 2),
            _ => (0, 1),
        };

        float rangeX = Math.Max(Axis(cloud.BoundsMax, ha) - Axis(cloud.BoundsMin, ha), 1e-6f);
        float rangeY = Math.Max(Axis(cloud.BoundsMax, va) - Axis(cloud.BoundsMin, va), 1e-6f);
        float scale = Math.Min(width / rangeX, height / rangeY) * 0.95f * _zoom;

        float centerX = Axis(cloud.Center, ha), centerY = Axis(cloud.Center, va);
        float offsetX = width / 2f + (float)_pan.X;
        float offsetY = height / 2f + (float)_pan.Y;

        var p = cloud.Positions;
        for (int i = 0; i < p.Length; i += 3)
        {
            int x = (int)((p[i + ha] - centerX) * scale + offsetX);
            int y = (int)(offsetY - (p[i + va] - centerY) * scale);
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

    private static float Axis(System.Numerics.Vector3 v, int index) => index switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private void CopyToBitmap(int width, int height)
    {
        using var fb = _bitmap!.Lock();
        for (int row = 0; row < height; row++)
            Marshal.Copy(_pixels, row * width, fb.Address + row * fb.RowBytes, width);
    }


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
        _pan += (p - last) * scaling;
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
