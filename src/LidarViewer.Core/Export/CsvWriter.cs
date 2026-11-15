using System.Globalization;
using System.Text;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Export;

/// <summary>
/// Zapisuje chmure punktow do pliku tekstowego z kolumnami x, y, z
/// i opcjonalnie intensity. Format czyta CsvReader.
///
/// Zapisuje wspolrzedne swiata, czyli pozycje plus Origin - tak samo jak
/// PlyWriter, zeby eksport nie zmienial geometrii chmury.
/// </summary>
public static class CsvWriter
{
    public static void Write(string path, PointCloud cloud)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(cloud);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        Write(writer, cloud);
    }

    public static void Write(TextWriter writer, PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(cloud);

        bool hasIntensity = cloud.Intensity is not null;
        writer.Write(hasIntensity ? "x,y,z,intensity\n" : "x,y,z\n");

        (double ox, double oy, double oz) = cloud.Origin;
        byte[]? intensity = cloud.Intensity;
        float[] positions = cloud.Positions;

        var line = new StringBuilder(64);
        for (int i = 0; i < cloud.Count; i++)
        {
            int source = i * 3;
            line.Clear();
            line.Append(Number(positions[source] + (float)ox)).Append(',');
            line.Append(Number(positions[source + 1] + (float)oy)).Append(',');
            line.Append(Number(positions[source + 2] + (float)oz));
            if (hasIntensity)
            {
                line.Append(',').Append(intensity![i].ToString(CultureInfo.InvariantCulture));
            }
            writer.WriteLine(line.ToString());
        }
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
