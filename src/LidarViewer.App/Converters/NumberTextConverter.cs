using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LidarViewer.App.Converters;

public sealed class NumberTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double number)
            return string.Empty;

        return number.ToString("0.######", culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text)
            return BindingOperations.DoNothing;

        text = text.Trim();
        if (text.Length == 0)
            return BindingOperations.DoNothing;

        if (double.TryParse(text, NumberStyles.Float, culture, out double result))
            return result;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return result;

        if (text.Contains(','))
        {
            string dot = text.Replace(',', '.');
            if (double.TryParse(dot, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return result;
        }

        return BindingOperations.DoNothing;
    }
}
