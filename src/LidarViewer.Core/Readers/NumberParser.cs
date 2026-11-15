using System.Globalization;

namespace LidarViewer.Core.Readers;

internal static class NumberParser
{
    public static bool TryParse(string token, out double value)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        switch (token.Trim().ToLowerInvariant())
        {
            case "inf" or "+inf": value = double.PositiveInfinity; return true;
            case "-inf": value = double.NegativeInfinity; return true;
            default: value = 0; return false;
        }
    }

    public static double Parse(string token) =>
        TryParse(token, out double v) ? v : throw new InvalidDataException($"'{token}' nie jest liczbą.");
}
