using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LidarViewer.Core.Rendering;

namespace LidarViewer.App.Controls;

public sealed class ColorLegend : Panel
{
    private const double BarHeight = 14;
    private const int StopsPerMap = 16;

    private readonly Grid _root;
    private readonly TextBlock _title = new() { FontSize = 11, Foreground = Brushes.White };
    private readonly Border _bar = new()
    {
        Height = BarHeight,
        BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
        BorderThickness = new Thickness(1),
    };
    private readonly TextBlock _minLabel = new() { FontSize = 10, Foreground = Brushes.LightGray };
    private readonly TextBlock _maxLabel = new()
    {
        FontSize = 10,
        Foreground = Brushes.LightGray,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    public static readonly StyledProperty<ColorSettings?> ColorSettingsProperty =
        AvaloniaProperty.Register<ColorLegend, ColorSettings?>(nameof(ColorSettings));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ColorLegend, string?>(nameof(Title));

    public ColorSettings? ColorSettings
    {
        get => GetValue(ColorSettingsProperty);
        set => SetValue(ColorSettingsProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    static ColorLegend()
    {
        ColorSettingsProperty.Changed.AddClassHandler<ColorLegend>((view, _) => view.Refresh());
        TitleProperty.Changed.AddClassHandler<ColorLegend>((view, _) => view.Refresh());
    }

    public ColorLegend()
    {
        _root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
        };

        Grid.SetRow(_title, 0);
        Grid.SetColumnSpan(_title, 2);
        Grid.SetRow(_bar, 1);
        Grid.SetColumnSpan(_bar, 2);
        Grid.SetRow(_minLabel, 2);
        Grid.SetColumn(_minLabel, 0);
        Grid.SetRow(_maxLabel, 2);
        Grid.SetColumn(_maxLabel, 1);

        _root.Children.Add(_title);
        _root.Children.Add(_bar);
        _root.Children.Add(_minLabel);
        _root.Children.Add(_maxLabel);

        Children.Add(_root);
        Width = 280;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _root.Measure(new Size(Width, availableSize.Height));
        return new Size(Width, _root.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void Refresh()
    {
        var settings = ColorSettings;
        if (settings is null)
            return;

        _title.Text = Title ?? string.Empty;
        _bar.Background = BuildBrush(settings.Map);
        _minLabel.Text = Format(settings.Min);
        _maxLabel.Text = Format(settings.Max);
    }

    private static IBrush BuildBrush(ColormapKind map)
    {
        var lut = Colormaps.Build(map);
        var stops = new GradientStops();

        for (int i = 0; i < StopsPerMap; i++)
        {
            float t = (float)i / (StopsPerMap - 1);
            var color = lut[(int)(t * (lut.Length - 1) + 0.5f)];
            stops.Add(new GradientStop(Color.FromRgb(color.R, color.G, color.B), t));
        }

        return new LinearGradientBrush
        {
            GradientStops = stops,
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };
    }

    private static string Format(float value) =>
        value.ToString("0.###", CultureInfo.CurrentCulture);
}
