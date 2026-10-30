using System.Globalization;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.IO;

public static class CsvPointCloudReader
{
    public static async Task<IReadOnlyList<LidarPoint>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var points = new List<LidarPoint>();
        using var reader = new StreamReader(path);
        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split(',');
            if (lineNumber == 1 && columns.Any(column =>
                    string.Equals(column.Trim(), "x", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var coordinateOffset = columns.Length >= 5 ? 1 : 0;
            if (columns.Length < coordinateOffset + 3 ||
                !TryParse(columns[coordinateOffset], out var x) ||
                !TryParse(columns[coordinateOffset + 1], out var y) ||
                !TryParse(columns[coordinateOffset + 2], out var z))
            {
                throw new InvalidDataException($"CSV line {lineNumber} does not contain valid X, Y, and Z values.");
            }

            var intensity = 0d;
            if (columns.Length > coordinateOffset + 3 &&
                !TryParse(columns[coordinateOffset + 3], out intensity))
            {
                throw new InvalidDataException($"CSV line {lineNumber} contains an invalid intensity value.");
            }

            points.Add(new LidarPoint(x, y, z, intensity));
        }

        return points;
    }

    private static bool TryParse(string value, out double result) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
        double.IsFinite(result);
}
