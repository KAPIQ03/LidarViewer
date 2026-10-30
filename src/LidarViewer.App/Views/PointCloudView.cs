using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LidarViewer.Core.Models;

namespace LidarViewer.App.Views;

public sealed class PointCloudView : Control
{
    public static readonly StyledProperty<IReadOnlyList<LidarPoint>> PointsProperty =
        AvaloniaProperty.Register<PointCloudView, IReadOnlyList<LidarPoint>>(
            nameof(Points),
            Array.Empty<LidarPoint>());

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#0B1119"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#263342")), 1);
    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.Parse("#637386")), 1);
    private static readonly IBrush LowIntensityBrush = new SolidColorBrush(Color.Parse("#47D7C2"));
    private static readonly IBrush MediumIntensityBrush = new SolidColorBrush(Color.Parse("#68A7FF"));
    private static readonly IBrush HighIntensityBrush = new SolidColorBrush(Color.Parse("#F3C969"));
    private static readonly IBrush ZeroIntensityBrush = new SolidColorBrush(Color.Parse("#A9B6C5"));

    private bool _dragging;
    private bool _panMode;
    private Point _lastPosition;
    private double _yaw = -0.75;
    private double _pitch = 0.35;
    private double _zoom = 1;
    private double _panX;
    private double _panY;

    static PointCloudView()
    {
        AffectsRender<PointCloudView>(PointsProperty);
    }

    public IReadOnlyList<LidarPoint> Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public void ResetCamera()
    {
        _yaw = -0.75;
        _pitch = 0.35;
        _zoom = 1;
        _panX = 0;
        _panY = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        context.DrawRectangle(BackgroundBrush, null, new Rect(bounds.Size));

        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DrawGrid(context, width, height);
        var points = Points;
        if (points.Count == 0)
        {
            return;
        }

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var minZ = points.Min(point => point.Z);
        var maxZ = points.Max(point => point.Z);
        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;
        var centerZ = (minZ + maxZ) / 2;
        var span = Math.Max(Math.Max(maxX - minX, maxY - minY), maxZ - minZ);
        if (span < 0.000001)
        {
            span = 1;
        }

        var scale = Math.Min(width, height) * 0.42 / span * _zoom;
        var cameraDistance = span * 3.5;
        var cosYaw = Math.Cos(_yaw);
        var sinYaw = Math.Sin(_yaw);
        var cosPitch = Math.Cos(_pitch);
        var sinPitch = Math.Sin(_pitch);
        var step = Math.Max(1, points.Count / 100_000);

        for (var index = 0; index < points.Count; index += step)
        {
            var point = points[index];
            var x = point.X - centerX;
            var y = point.Y - centerY;
            var z = point.Z - centerZ;
            var rotatedX = x * cosYaw - y * sinYaw;
            var rotatedY = x * sinYaw + y * cosYaw;
            var rotatedZ = rotatedY * sinPitch + z * cosPitch;
            rotatedY = rotatedY * cosPitch - z * sinPitch;
            var depth = cameraDistance - rotatedZ;
            if (depth <= cameraDistance * 0.15)
            {
                continue;
            }

            var perspective = cameraDistance / depth;
            var screenPoint = new Point(
                width / 2 + rotatedX * scale * perspective + _panX,
                height / 2 - rotatedY * scale * perspective + _panY);
            if (screenPoint.X < -4 || screenPoint.X > width + 4 ||
                screenPoint.Y < -4 || screenPoint.Y > height + 4)
            {
                continue;
            }

            var brush = point.Intensity <= 0
                ? ZeroIntensityBrush
                : point.Intensity < 0.33 ? LowIntensityBrush
                : point.Intensity < 0.67 ? MediumIntensityBrush
                : HighIntensityBrush;
            var radius = Math.Clamp(1.5 * perspective, 1, 3);
            context.DrawEllipse(brush, null, screenPoint, radius, radius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed)
        {
            return;
        }

        _dragging = true;
        _panMode = properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _lastPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        var deltaX = position.X - _lastPosition.X;
        var deltaY = position.Y - _lastPosition.Y;
        if (_panMode)
        {
            _panX += deltaX;
            _panY += deltaY;
        }
        else
        {
            _yaw += deltaX * 0.008;
            _pitch = Math.Clamp(_pitch + deltaY * 0.008, -Math.PI / 2 + 0.05, Math.PI / 2 - 0.05);
        }

        _lastPosition = position;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.12 : 0.89), 0.1, 30);
        InvalidateVisual();
        e.Handled = true;
    }

    private static void DrawGrid(DrawingContext context, double width, double height)
    {
        var origin = new Point(width / 2, height / 2);
        const double spacing = 42;
        for (var index = -12; index <= 12; index++)
        {
            var offset = index * spacing;
            context.DrawLine(GridPen, new Point(origin.X + offset, 0), new Point(origin.X + offset, height));
            context.DrawLine(GridPen, new Point(0, origin.Y + offset), new Point(width, origin.Y + offset));
        }

        context.DrawLine(AxisPen, new Point(origin.X, 0), new Point(origin.X, height));
        context.DrawLine(AxisPen, new Point(0, origin.Y), new Point(width, origin.Y));
    }
}
