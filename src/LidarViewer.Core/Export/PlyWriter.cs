using System.Buffers.Binary;
using System.Text;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Export;

/// <summary>
/// Zapisuje chmure punktow do pliku PLY w formacie binary_little_endian.
/// Format jest ten sam, ktory czyta PlyReader, wiec zapisana chmura daje
/// sie od razu wczytac w aplikacji.
///
/// Zapisuje wspolrzedne swiata, czyli pozycje plus Origin. Dzieki temu
/// eksport nie zmienia geometrii ani odleglosci punktow od czujnika.
/// </summary>
public static class PlyWriter
{
    private const int PointsPerChunk = 4096;

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
        WriteHeader(stream, cloud.Count, hasIntensity);
        WriteVertices(stream, cloud, hasIntensity);
    }

    private static void WriteHeader(Stream stream, int count, bool hasIntensity)
    {
        var header = new StringBuilder();
        header.Append("ply\n");
        header.Append("format binary_little_endian 1.0\n");
        header.Append($"element vertex {count}\n");
        header.Append("property float x\n");
        header.Append("property float y\n");
        header.Append("property float z\n");
        if (hasIntensity)
            header.Append("property uchar intensity\n");
        header.Append("end_header\n");

        byte[] bytes = Encoding.ASCII.GetBytes(header.ToString());
        stream.Write(bytes);
    }

    private static void WriteVertices(Stream stream, PointCloud cloud, bool hasIntensity)
    {
        int stride = hasIntensity ? 13 : 12;
        byte[] buffer = new byte[PointsPerChunk * stride];

        (double ox, double oy, double oz) = cloud.Origin;
        byte[]? intensity = cloud.Intensity;
        float[] positions = cloud.Positions;

        int written = 0;
        while (written < cloud.Count)
        {
            int chunk = Math.Min(PointsPerChunk, cloud.Count - written);
            for (int i = 0; i < chunk; i++)
            {
                int source = (written + i) * 3;
                int offset = i * stride;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), positions[source] + (float)ox);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 4), positions[source + 1] + (float)oy);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 8), positions[source + 2] + (float)oz);
                if (hasIntensity)
                    buffer[offset + 12] = intensity![written + i];
            }

            stream.Write(buffer, 0, chunk * stride);
            written += chunk;
        }
    }
}
