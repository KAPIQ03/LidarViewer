using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LidarViewer.App.Converters;

public sealed class EnumLabelConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Solid"] = "Jednolity",
        ["Intensity"] = "Intensywność",
        ["Distance"] = "Odległość od czujnika",
        ["Height"] = "Wysokość (Z)",
        ["LaserId"] = "Numer lasera",
        ["Azimuth"] = "Azymut",
        ["Time"] = "Czas w klatce",
        ["Grayscale"] = "Skala szarości",
        ["Vlp16"] = "VLP-16",
        ["Hdl32E"] = "HDL-32E",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? string.Empty : Labels.GetValueOrDefault(value.ToString() ?? "", value.ToString() ?? "");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
