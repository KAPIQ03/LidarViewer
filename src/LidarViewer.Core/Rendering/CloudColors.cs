using LidarViewer.Core.Models;

namespace LidarViewer.Core.Rendering;

public static class CloudColors
{
    private const byte FallbackGray = 128;

    public static byte[] Build(PointCloud cloud, ColorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(settings);

        int count = cloud.Count;
        var rgba = new byte[count * 4];

        if (settings.Mode == ColorMode.Solid)
        {
            for (int i = 0; i < count; i++)
            {
                rgba[i * 4] = settings.Solid.R;
                rgba[i * 4 + 1] = settings.Solid.G;
                rgba[i * 4 + 2] = settings.Solid.B;
                rgba[i * 4 + 3] = settings.Solid.A;
            }
            return rgba;
        }

        ScalarKind kind = settings.Mode == ColorMode.Intensity
            ? ScalarKind.Intensity
            : ScalarKind.Distance;

        var attribute = cloud.GetAttribute(kind);
        if (attribute is null)
        {
            for (int i = 0; i < count; i++)
            {
                rgba[i * 4] = FallbackGray;
                rgba[i * 4 + 1] = FallbackGray;
                rgba[i * 4 + 2] = FallbackGray;
                rgba[i * 4 + 3] = 0xFF;
            }
            return rgba;
        }

        var ramp = new ColorRamp(settings.Map, settings.Min, settings.Max);
        var values = attribute.Values;

        // Tablica atrybutu moze byc krotsza niz liczba punktow (uszkodzony plik),
        // wiec nie wolno indeksowac jej poza zakresem.
        int colored = Math.Min(count, values.Length);
        for (int i = 0; i < colored; i++)
        {
            var color = ramp.ColorAt(values[i]);
            rgba[i * 4] = color.R;
            rgba[i * 4 + 1] = color.G;
            rgba[i * 4 + 2] = color.B;
            rgba[i * 4 + 3] = 0xFF;
        }

        for (int i = colored; i < count; i++)
        {
            rgba[i * 4] = FallbackGray;
            rgba[i * 4 + 1] = FallbackGray;
            rgba[i * 4 + 2] = FallbackGray;
            rgba[i * 4 + 3] = 0xFF;
        }

        return rgba;
    }
}
