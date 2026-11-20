using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LidarViewer.Core.Rendering;

namespace LidarViewer.App.Controls;

public sealed class GridLabelOverlay : Control
{
    private const double MinSpacing = 28;

    public static readonly StyledProperty<IReadOnlyList<ScreenLabel>?> LabelsProperty =
        AvaloniaProperty.Register<GridLabelOverlay, IReadOnlyList<ScreenLabel>?>(nameof(Labels));

    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC6, 0xB8));
    private static readonly IBrush Backdrop = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10));
    private static readonly Typeface Face = new(FontFamily.Default);

    static GridLabelOverlay()
    {
        AffectsRender<GridLabelOverlay>(LabelsProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<GridLabelOverlay>(false);
    }

    public IReadOnlyList<ScreenLabel>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Labels is not { Count: > 0 } labels)
            return;

        var placed = new List<Point>();
        foreach (ScreenLabel label in labels)
        {
            var anchor = new Point(label.X, label.Y);
            if (placed.Exists(p => Math.Abs(p.X - anchor.X) + Math.Abs(p.Y - anchor.Y) < MinSpacing))
                continue;

            placed.Add(anchor);
            var text = new FormattedText(label.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, TextBrush);
            var origin = new Point(anchor.X + 4, anchor.Y - text.Height - 2);
            context.FillRectangle(Backdrop, new Rect(origin.X - 3, origin.Y - 1, text.Width + 6, text.Height + 2), 3);
            context.DrawText(text, origin);
        }
    }
}
