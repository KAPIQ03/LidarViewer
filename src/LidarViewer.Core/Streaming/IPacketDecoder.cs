using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

public interface IPacketDecoder
{
    string Name { get; }

    bool TryDecode(ReadOnlySpan<byte> packet, out PointCloud? cloud);
}
