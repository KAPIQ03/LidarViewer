using System.Buffers.Binary;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

public sealed class SimpleCloudDecoder : IPacketDecoder
{
    public string Name => "Wlasny protokul (LVLR)";

    public bool TryDecode(ReadOnlySpan<byte> packet, out PointCloud? cloud)
    {
        cloud = null;

        if (packet.Length < SimpleCloudProtocol.HeaderSize)
            return false;

        if (!packet[..4].SequenceEqual(SimpleCloudProtocol.Magic))
            return false;

        if (packet[4] != SimpleCloudProtocol.Version)
            return false;

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(packet[6..10]);
        long expected = SimpleCloudProtocol.HeaderSize + (long)count * SimpleCloudProtocol.PointSize;
        if (expected != packet.Length)
            return false;

        int n = (int)count;
        var positions = new float[n * 3];
        var intensity = new byte[n];

        int offset = SimpleCloudProtocol.HeaderSize;
        for (int i = 0; i < n; i++)
        {
            positions[i * 3] = BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(offset, 4));
            positions[i * 3 + 1] = BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(offset + 4, 4));
            positions[i * 3 + 2] = BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(offset + 8, 4));
            intensity[i] = packet[offset + 12];
            offset += SimpleCloudProtocol.PointSize;
        }

        cloud = new PointCloud(positions, intensity);
        return true;
    }
}
