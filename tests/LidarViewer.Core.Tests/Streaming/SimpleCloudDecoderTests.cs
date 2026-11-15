using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class SimpleCloudDecoderTests
{
    private readonly SimpleCloudDecoder _decoder = new();

    private static float[] Positions() => [1f, 2f, 3f, -4f, 5.5f, -6f];

    [Fact]
    public void EncodeWritesHeaderWithPointCount()
    {
        byte[] packet = SimpleCloudProtocol.Encode(Positions(), [10, 20]);

        Assert.Equal(SimpleCloudProtocol.HeaderSize + 2 * SimpleCloudProtocol.PointSize, packet.Length);
        Assert.Equal("LVLR"u8.ToArray(), packet[..4]);
        Assert.Equal(SimpleCloudProtocol.Version, packet[4]);
        Assert.Equal(2u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(6, 4)));
    }

    [Fact]
    public void DecodeReadsPositionsAndIntensity()
    {
        byte[] packet = SimpleCloudProtocol.Encode(Positions(), [10, 20]);

        bool ok = _decoder.TryDecode(packet, out var cloud);

        Assert.True(ok);
        Assert.NotNull(cloud);
        Assert.Equal(2, cloud!.Count);
        Assert.Equal(new[] { 1f, 2f, 3f, -4f, 5.5f, -6f }, cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Fact]
    public void DecodeAcceptsPacketWithoutIntensity()
    {
        byte[] packet = SimpleCloudProtocol.Encode(Positions(), null);

        bool ok = _decoder.TryDecode(packet, out var cloud);

        Assert.True(ok);
        Assert.Equal(2, cloud!.Count);
    }

    [Fact]
    public void DecodeRejectsWrongMagic()
    {
        byte[] packet = SimpleCloudProtocol.Encode(Positions(), [10, 20]);
        packet[0] = (byte)'X';

        Assert.False(_decoder.TryDecode(packet, out var cloud));
        Assert.Null(cloud);
    }

    [Fact]
    public void DecodeRejectsShortPacket()
    {
        byte[] packet = SimpleCloudProtocol.Encode(Positions(), [10, 20]);

        Assert.False(_decoder.TryDecode(packet.AsSpan(0, 5).ToArray(), out _));
    }

    [Fact]
    public void DecodeRejectsTruncatedPayload()
    {
        byte[] packet = SimpleCloudProtocol.Encode(Positions(), [10, 20]);

        Assert.False(_decoder.TryDecode(packet.AsSpan(0, packet.Length - 1).ToArray(), out _));
    }

    [Fact]
    public void DecodeRejectsUnknownVersion()
    {
        byte[] packet = SimpleCloudProtocol.Encode(Positions(), [10, 20]);
        packet[4] = 99;

        Assert.False(_decoder.TryDecode(packet, out _));
    }
}
