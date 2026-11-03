using LidarViewer.Core.Models;
using LidarViewer.Core.Vtp;

namespace LidarViewer.Core.Readers;

/// <summary>
/// Jedno wejście do wczytywania chmur punktów: wybiera czytnik po rozszerzeniu pliku,
/// a wynik oczyszcza z punktów o nieskończonych/NaN współrzędnych. Interfejs użytkownika
/// zna tylko tę klasę, więc dodanie nowego formatu = dopisanie jednej linijki poniżej.
/// </summary>
public static class PointCloudLoader
{
    private static readonly Dictionary<string, Func<string, PointCloud>> Readers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".vtp"] = VtpReader.Read,
            [".vtk"] = VtkReader.Read,
            [".csv"] = CsvReader.Read,
            [".ply"] = PlyReader.Read,
            [".pcd"] = PcdReader.Read,
            [".las"] = LasReader.Read,
        };
    
    public static IReadOnlyCollection<string> SupportedExtensions => Readers.Keys;
    
    public static PointCloud Read(string path)
    {
        string extension = Path.GetExtension(path);
        if (!Readers.TryGetValue(extension, out var reader))
            throw new NotSupportedException(
                $"Nieobsługiwane rozszerzenie '{extension}'. Obsługiwane: {string.Join(", ", Readers.Keys)}.");

        return reader(path).WithoutInvalidPoints();
    }
}
