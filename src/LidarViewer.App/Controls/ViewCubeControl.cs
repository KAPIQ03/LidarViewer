using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LidarViewer.Core.Rendering;

namespace LidarViewer.App.Controls;

/// <summary>
/// Kostka orientacji w rogu widoku 3D (jak w Blenderze): pokazuje, jak obrocona jest scena,
/// a klikniecie sciany ustawia widok wzdluz tej osi. Cala matematyka jest w <see cref="ViewCube"/>.
/// </summary>
public sealed class ViewCubeControl : Control
{
    private const double CubeSize = 96;

    public static readonly StyledProperty<float> YawProperty =
        AvaloniaProperty.Register<ViewCubeControl, float>(nameof(Yaw));

    public static readonly StyledProperty<float> PitchProperty =
        AvaloniaProperty.Register<ViewCubeControl, float>(nameof(Pitch));

    private ViewCubeFace? _hover;

    static ViewCubeControl()
    {
        AffectsRender<ViewCubeControl>(YawProperty, PitchProperty);
    }

    public ViewCubeControl()
    {
        Width = Height = CubeSize;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public float Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    public float Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    /// <summary>Uzytkownik kliknal sciane - kierunek patrzenia kamery.</summary>
    public event Action<Vector3>? DirectionRequested;

    private static Color AxisColor(Vector3 axis)
    {
        if (MathF.Abs(axis.X) > 0.5f) return Color.Parse("#E5484D");
        if (MathF.Abs(axis.Y) > 0.5f) return Color.Parse("#46A758");
        return Color.Parse("#3E63DD");
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var faces = ViewCube.Project(Yaw, Pitch, (float)CubeSize);

        foreach (ViewCubeFace face in faces)
        {
            Color color = AxisColor(face.Axis);
            bool negative = face.Axis.X + face.Axis.Y + face.Axis.Z < 0;
            byte alpha = (byte)(negative ? 150 : 225);
            if (ReferenceEquals(face, _hover))
                alpha = 255;

            var geometry = new StreamGeometry();
            using (StreamGeometryContext g = geometry.Open())
            {
                g.BeginFigure(ToPoint(face.Corners[0]), true);
                for (int i = 1; i < 4; i++)
                    g.LineTo(ToPoint(face.Corners[i]));
                g.EndFigure(true);
            }

            var fill = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 20, 22, 26)), 1.5, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(fill, pen, geometry);

            var text = new FormattedText(
                face.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 13, Brushes.White);
            Vector2 c = face.Center;
            context.DrawText(text, new Point(c.X - text.Width / 2, c.Y - text.Height / 2));
        }
    }

    private static Point ToPoint(Vector2 v) => new(v.X, v.Y);

    private ViewCubeFace? FaceAt(Point p) =>
        ViewCube.HitTest(ViewCube.Project(Yaw, Pitch, (float)CubeSize), new Vector2((float)p.X, (float)p.Y));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ViewCubeFace? face = FaceAt(e.GetPosition(this));
        if (face?.Label != _hover?.Label)
        {
            _hover = face;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        ViewCubeFace? face = FaceAt(e.GetPosition(this));
        if (face is not null)
        {
            DirectionRequested?.Invoke(face.LookDirection);
            e.Handled = true;
        }
    }
}
