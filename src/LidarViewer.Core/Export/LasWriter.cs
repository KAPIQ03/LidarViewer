using System.Buffers.Binary;
using System.Text;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Export;

public static class LasWriter
{
    private const int HeaderSize = 227;
    private const int RecordLength = 20;
    private const double Scale = 0.001;

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

        int count = cloud.Count;
        (double ox, double oy, double oz) = cloud.Origin;

        double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;
        if (count > 0)
        {
            minX = minY = minZ = double.MaxValue;
            maxX = maxY = maxZ = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                double x = cloud.Positions[i * 3] + ox, y = cloud.Positions[i * 3 + 1] + oy, z = cloud.Positions[i * 3 + 2] + oz;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }

            double range = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            if (range / Scale > int.MaxValue)
                throw new InvalidOperationException("Chmura jest zbyt duża dla formatu LAS ze skalą 1 mm.");
        }

        byte[] header = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("LASF").CopyTo(header, 0);
        header[24] = 1;
        header[25] = 2;
        Encoding.ASCII.GetBytes("LidarViewer").CopyTo(header, 58);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(90), (ushort)DateTime.UtcNow.DayOfYear);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(92), (ushort)DateTime.UtcNow.Year);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(94), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(96), HeaderSize);
        header[104] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(105), RecordLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(107), (uint)count);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(111), (uint)count);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(131), Scale);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(139), Scale);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(147), Scale);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(155), minX);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(163), minY);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(171), minZ);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(179), maxX);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(187), minX);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(195), maxY);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(203), minY);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(211), maxZ);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(219), minZ);
        stream.Write(header);

        byte[] buffer = new byte[4096 * RecordLength];
        int written = 0;
        while (written < count)
        {
            int chunk = Math.Min(4096, count - written);
            Array.Clear(buffer, 0, chunk * RecordLength);
            for (int i = 0; i < chunk; i++)
            {
                int p = (written + i) * 3;
                Span<byte> record = buffer.AsSpan(i * RecordLength, RecordLength);
                BinaryPrimitives.WriteInt32LittleEndian(record, (int)Math.Round((cloud.Positions[p] + ox - minX) / Scale));
                BinaryPrimitives.WriteInt32LittleEndian(record[4..], (int)Math.Round((cloud.Positions[p + 1] + oy - minY) / Scale));
                BinaryPrimitives.WriteInt32LittleEndian(record[8..], (int)Math.Round((cloud.Positions[p + 2] + oz - minZ) / Scale));
                if (cloud.Intensity is not null)
                    BinaryPrimitives.WriteUInt16LittleEndian(record[12..], cloud.Intensity[written + i]);
                record[14] = 0x09;
                if (cloud.Category is not null)
                    record[15] = (byte)(cloud.Category[written + i] & 0x1F);
            }

            stream.Write(buffer, 0, chunk * RecordLength);
            written += chunk;
        }
    }
}
