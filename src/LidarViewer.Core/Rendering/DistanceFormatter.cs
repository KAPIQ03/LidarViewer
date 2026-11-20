using System.Globalization;

namespace LidarViewer.Core.Rendering;

public static class DistanceFormatter
{
    public static string Format(double meters)
    {
        if (double.IsNaN(meters) || double.IsInfinity(meters))
            return "—";

        double abs = Math.Abs(meters);
        return abs switch
        {
            0 => "0 m",
            < 0.01 => Number(meters * 1000, "0.#") + " mm",
            < 1 => Number(meters * 100, "0.#") + " cm",
            < 1000 => Number(meters, "0.##") + " m",
            _ => Number(meters / 1000, "0.###") + " km",
        };
    }

    private static string Number(double value, string format) =>
        value.ToString(format, CultureInfo.GetCultureInfo("pl-PL"));
}
