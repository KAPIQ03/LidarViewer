using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LidarViewer.App.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
