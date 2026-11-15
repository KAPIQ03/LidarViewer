using System.Buffers.Binary;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Wlasny protokol przesylania chmury punktow po UDP.
///
/// Naglowek (10 bajtow):
///   0..3  magika "LVLR"
///   4     wersja protokolu
///   5     flagi (zarezerwowane)
///   6..9  liczba punktow (uint32, little endian)
///
/// Nastepnie dla kazdego punktu 13 bajtow:
///   x, y, z (float32, little endian), intensywnosc (1 bajt)
/// </summary>
public static class SimpleCloudProtocol
{
    public const int HeaderSize = 10;
    public const int PointSize = 13;
    public const byte Version = 1;

    public static ReadOnlySpan<byte> Magic => "LVLR"u8;

    public static byte[] Encode(ReadOnlySpan<float> positions, byte[]? intensity = null)
    {
        if (positions.Length % 3 != 0)
            throw new ArgumentException("Tablica pozycji musi miec dlugosc wielokrotnosci 3.", nameof(positions));

        int count = positions.Length / 3;
        if (intensity is not null && intensity.Length != count)
            throw new ArgumentException("Liczba wartosci intensywnosci musi rowac sie z liczba punktow.", nameof(intensity));

        byte[] packet = new byte[HeaderSize + count * PointSize];
        Magic.CopyTo(packet);
        packet[4] = Version;
        packet[5] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(6, 4), (uint)count);

        int offset = HeaderSize;
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(offset, 4), positions[i * 3]);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(offset + 4, 4), positions[i * 3 + 1]);
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(offset + 8, 4), positions[i * 3 + 2]);
            packet[offset + 12] = intensity is not null ? intensity[i] : (byte)0;
            offset += PointSize;
        }

        return packet;
    }
}
