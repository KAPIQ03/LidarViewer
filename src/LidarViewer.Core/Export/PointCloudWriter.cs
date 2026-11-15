using LidarViewer.Core.Models;

namespace LidarViewer.Core.Export;

/// <summary>
/// Zapisuje chmure punktow do pliku. Format wybiera po rozszerzeniu, wiec
/// ten sam kod obsluguje eksport do PLY i do pliku tekstowego.
/// </summary>
public static class PointCloudWriter
{
    private static readonly Dictionary<string, Action<PointCloud, string>> Writers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".ply"] = (cloud, path) => PlyWriter.Write(path, cloud),
            [".csv"] = (cloud, path) => CsvWriter.Write(path, cloud),
            [".xyz"] = (cloud, path) => CsvWriter.Write(path, cloud),
            [".pcd"] = (cloud, path) => PcdWriter.Write(path, cloud),
            [".las"] = (cloud, path) => LasWriter.Write(path, cloud),
        };

    public static IReadOnlyCollection<string> SupportedExtensions => Writers.Keys;

    public static void Write(PointCloud cloud, string path)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string extension = Path.GetExtension(path);
        if (!Writers.TryGetValue(extension, out var writer))
            throw new NotSupportedException(
                $"Nieobsługiwane rozszerzenie '{extension}'. Obsługiwane: {string.Join(", ", Writers.Keys)}.");

        writer(cloud, path);
    }
}
