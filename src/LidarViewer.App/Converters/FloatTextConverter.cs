using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LidarViewer.App.Converters;

public sealed class FloatTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not float number)
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

        if (float.TryParse(text, NumberStyles.Float, culture, out float result))
            return result;

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return result;

        // Ostatnia proba: przecinek jako separator dziesietny. System z kultura
        // angielska odrzucilby "12,5", a uzytkownik pisze po polsku.
        if (text.Contains(','))
        {
            string dot = text.Replace(',', '.');
            if (float.TryParse(dot, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return result;
        }

        // Niepoprawna wartosc nie moze trafic do modelu - inaczej pole
        // po cichu cofnie sie do poprzedniej wartosci.
        return BindingOperations.DoNothing;
    }
}
