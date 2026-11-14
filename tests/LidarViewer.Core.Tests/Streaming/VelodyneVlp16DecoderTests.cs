using System.Buffers.Binary;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class VelodyneVlp16DecoderTests
{
    private readonly VelodyneVlp16Decoder _decoder = new();

    /// <summary>
    /// Buduje pakiet VLP-16: 12 blokow po 100 B, naglowek banku 0xEEFF,
    /// azymut w dziesiatych stopnia, dystans w jednostkach 2 mm.
    /// </summary>
    private static byte[] BuildPacket(int blockCount = 12, ushort? azimuth = null, ushort distance = 1000, byte reflectivity = 77)
    {
        byte[] packet = new byte[VelodyneVlp16Decoder.PacketSize];
        for (int block = 0; block < blockCount; block++)
        {
            int offset = block * VelodyneVlp16Decoder.BlockSize;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset, 2), VelodyneVlp16Decoder.UpperBank);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset + 2, 2), azimuth ?? (ushort)(block * 200));
            for (int firing = 0; firing < 2; firing++)
            {
                for (int laser = 0; laser < 16; laser++)
                {
                    int scan = offset + 4 + (firing * 16 + laser) * 3;
                    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(scan, 2), distance);
                    packet[scan + 2] = reflectivity;
                }
            }
        }
        return packet;
    }

    [Fact]
    public void DecodeProducesPointPerLaser()
    {
        byte[] packet = BuildPacket();

        bool ok = _decoder.TryDecode(packet, out var cloud);

        Assert.True(ok);
        Assert.NotNull(cloud);
        Assert.Equal(12 * 32, cloud!.Count);
    }

    [Fact]
    public void DecodeUsesDistanceResolution()
    {
        byte[] packet = BuildPacket(distance: 1000);

        _decoder.TryDecode(packet, out var cloud);

        // 1000 jednostek * 0.002 m = 2 m
        float radius = MathF.Sqrt(
            cloud!.Positions[0] * cloud.Positions[0] +
            cloud.Positions[1] * cloud.Positions[1] +
            cloud.Positions[2] * cloud.Positions[2]);

        Assert.Equal(2f, radius, 3);
    }

    [Fact]
    public void DecodePlacesPointOnVerticalAngleOfLaser()
    {
        byte[] packet = BuildPacket();

        _decoder.TryDecode(packet, out var cloud);

        // Laser 0 ma kat pionowy -15 stopni, laser 1 ma +1 stopien.
        float zLaser0 = cloud!.Positions[2];
        float zLaser1 = cloud.Positions[5];

        Assert.True(zLaser0 < 0);
        Assert.True(zLaser1 > 0);
        Assert.Equal(2f * MathF.Sin(-15f * MathF.PI / 180f), zLaser0, 4);
        Assert.Equal(2f * MathF.Sin(1f * MathF.PI / 180f), zLaser1, 4);
    }

    [Fact]
    public void DecodeSkipsMissingReturns()
    {
        byte[] packet = BuildPacket(distance: 0);

        bool ok = _decoder.TryDecode(packet, out var cloud);

        Assert.True(ok);
        Assert.Equal(0, cloud!.Count);
    }

    [Fact]
    public void DecodeRejectsPacketWithWrongBankHeader()
    {
        byte[] packet = BuildPacket();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), 0xDDFF);

        Assert.False(_decoder.TryDecode(packet, out var cloud));
        Assert.Null(cloud);
    }

    [Fact]
    public void DecodeRejectsShortPacket()
    {
        byte[] packet = BuildPacket();

        Assert.False(_decoder.TryDecode(packet.AsSpan(0, 100).ToArray(), out _));
    }

    [Fact]
    public void DecodeReadsReflectivityAsIntensity()
    {
        byte[] packet = BuildPacket(reflectivity: 200);

        _decoder.TryDecode(packet, out var cloud);

        Assert.Equal(200, cloud!.Intensity![0]);
    }
}
