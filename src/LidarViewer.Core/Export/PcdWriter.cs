using System.Buffers.Binary;
using System.Text;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Export;

/// <summary>
/// Zapisuje chmure do pliku PCD 0.7 (DATA binary): pola x, y, z oraz opcjonalnie intensity (float).
/// Wspolrzedne swiata (pozycja + Origin), jak w PlyWriter. Plik czyta PcdReader, PCL i CloudCompare.
/// </summary>
public static class PcdWriter
{
    public static void Write(string path, PointCloud cloud)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(cloud);

        using var file = File.Create(path);
        Write(file, cloud);
    }

    public static void Write(Stream stream, PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(cloud);

        bool hasIntensity = cloud.Intensity is not null;
        var header = new StringBuilder();
        header.Append("# .PCD v0.7 - Point Cloud Data file format\n");
        header.Append("VERSION 0.7\n");
        header.Append(hasIntensity ? "FIELDS x y z intensity\n" : "FIELDS x y z\n");
        header.Append(hasIntensity ? "SIZE 4 4 4 4\n" : "SIZE 4 4 4\n");
        header.Append(hasIntensity ? "TYPE F F F F\n" : "TYPE F F F\n");
        header.Append(hasIntensity ? "COUNT 1 1 1 1\n" : "COUNT 1 1 1\n");
        header.Append($"WIDTH {cloud.Count}\n");
        header.Append("HEIGHT 1\n");
        header.Append("VIEWPOINT 0 0 0 1 0 0 0\n");
        header.Append($"POINTS {cloud.Count}\n");
        header.Append("DATA binary\n");
        stream.Write(Encoding.ASCII.GetBytes(header.ToString()));

        int stride = hasIntensity ? 16 : 12;
        byte[] buffer = new byte[4096 * stride];
        (double ox, double oy, double oz) = cloud.Origin;

        int written = 0;
        while (written < cloud.Count)
        {
            int chunk = Math.Min(4096, cloud.Count - written);
            for (int i = 0; i < chunk; i++)
            {
                int source = (written + i) * 3;
                int offset = i * stride;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), cloud.Positions[source] + (float)ox);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 4), cloud.Positions[source + 1] + (float)oy);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 8), cloud.Positions[source + 2] + (float)oz);
                if (hasIntensity)
                    BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 12), cloud.Intensity![written + i]);
            }

            stream.Write(buffer, 0, chunk * stride);
            written += chunk;
        }
    }
}
