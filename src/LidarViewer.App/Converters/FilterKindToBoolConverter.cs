using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using LidarViewer.Core.Filters;

namespace LidarViewer.App.Converters;

public sealed class FilterKindToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FilterParameterKind kind || parameter is null)
            return false;

        return Enum.TryParse(parameter.ToString(), ignoreCase: true, out FilterParameterKind wanted)
            && kind == wanted;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
