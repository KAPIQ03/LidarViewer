using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using LidarViewer.App.ViewModels;

namespace LidarViewer.App.Converters;

/// <summary>
/// Zamienia format pakietow na czytelna nazwe dla uzytkownika.
/// </summary>
public sealed class LiveProtocolToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            LiveProtocol.Wlasny => "Własny (LVLR)",
            LiveProtocol.VelodyneVlp16 => "Velodyne (kalibracja)",
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
