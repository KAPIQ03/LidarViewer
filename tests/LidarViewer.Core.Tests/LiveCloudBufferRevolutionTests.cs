using LidarViewer.Core.Models;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests;

public class LiveCloudBufferRevolutionTests
{
    private static PointCloud Packet(params float[] azimuths)
    {
        var positions = new float[azimuths.Length * 3];
        for (int i = 0; i < azimuths.Length; i++)
            positions[i * 3] = azimuths[i];
        return new PointCloud(positions) { Azimuth = azimuths };
    }

    [Fact]
    public void SnapshotIsLastCompleteRevolution()
    {
        var buffer = new LiveCloudBuffer(1000, byRevolution: true);

        buffer.Add(Packet(10, 20, 30));
        buffer.Add(Packet(340, 350));
        buffer.Add(Packet(5, 15));

        PointCloud snapshot = buffer.Snapshot();

        Assert.Equal([10f, 20f, 30f, 340f, 350f], snapshot.Azimuth);
    }

    [Fact]
    public void WrapInsideOnePacketSplitsIt()
    {
        var buffer = new LiveCloudBuffer(1000, byRevolution: true);

        buffer.Add(Packet(100, 200, 300));
        buffer.Add(Packet(350, 359, 2, 6));

        Assert.Equal([100f, 200f, 300f, 350f, 359f], buffer.Snapshot().Azimuth);
    }

    [Fact]
    public void NextRevolutionReplacesPrevious()
    {
        var buffer = new LiveCloudBuffer(1000, byRevolution: true);

        buffer.Add(Packet(10, 200));
        buffer.Add(Packet(350));
        buffer.Add(Packet(20, 180));
        buffer.Add(Packet(340));
        buffer.Add(Packet(10));

        Assert.Equal([20f, 180f, 340f], buffer.Snapshot().Azimuth);
    }

    [Fact]
    public void WithoutRevolutionModePointsAccumulate()
    {
        var buffer = new LiveCloudBuffer(1000);

        buffer.Add(Packet(10, 350));
        buffer.Add(Packet(5, 15));

        Assert.Equal(4, buffer.Snapshot().Count);
    }

    [Fact]
    public void BeforeFirstWrapSnapshotHoldsPartialRevolution()
    {
        var buffer = new LiveCloudBuffer(1000, byRevolution: true);

        buffer.Add(Packet(10, 20));

        Assert.Equal(2, buffer.Snapshot().Count);
    }

    [Fact]
    public void ClearForgetsCompletedRevolution()
    {
        var buffer = new LiveCloudBuffer(1000, byRevolution: true);
        buffer.Add(Packet(10, 350));
        buffer.Add(Packet(5));

        buffer.Clear();

        Assert.Equal(0, buffer.Snapshot().Count);
    }
}
