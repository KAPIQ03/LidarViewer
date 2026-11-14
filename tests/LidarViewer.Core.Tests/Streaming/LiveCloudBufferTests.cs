using LidarViewer.Core.Models;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class LiveCloudBufferTests
{
    private static PointCloud Cloud(params float[] positions) => new(positions);

    [Fact]
    public void AddAccumulatesPoints()
    {
        var buffer = new LiveCloudBuffer(10);

        buffer.Add(Cloud(0f, 0f, 0f));
        buffer.Add(Cloud(1f, 1f, 1f));

        Assert.Equal(2, buffer.Count);
        Assert.Equal(2, buffer.Snapshot().Count);
    }

    [Fact]
    public void SnapshotKeepsOrderOfAddedPoints()
    {
        var buffer = new LiveCloudBuffer(10);
        buffer.Add(Cloud(1f, 1f, 1f));
        buffer.Add(Cloud(2f, 2f, 2f));

        float[] positions = buffer.Snapshot().Positions;

        Assert.Equal(new[] { 1f, 1f, 1f, 2f, 2f, 2f }, positions);
    }

    [Fact]
    public void BufferDropsOldestPointsWhenFull()
    {
        var buffer = new LiveCloudBuffer(4);
        buffer.Add(Cloud(1f, 0f, 0f));
        buffer.Add(Cloud(2f, 0f, 0f));
        buffer.Add(Cloud(3f, 0f, 0f));

        Assert.Equal(3, buffer.Count);
        Assert.Equal(3, buffer.Snapshot().Count);
    }

    [Fact]
    public void BufferKeepsNewestPointsWhenFull()
    {
        var buffer = new LiveCloudBuffer(2);
        buffer.Add(Cloud(1f, 0f, 0f));
        buffer.Add(Cloud(2f, 0f, 0f));
        buffer.Add(Cloud(3f, 0f, 0f));

        float[] positions = buffer.Snapshot().Positions;

        Assert.Equal(new[] { 2f, 0f, 0f, 3f, 0f, 0f }, positions);
    }

    [Fact]
    public void AddIgnoresPointsOverCapacity()
    {
        var buffer = new LiveCloudBuffer(2);

        buffer.Add(Cloud(1f, 0f, 0f, 2f, 0f, 0f, 3f, 0f, 0f));

        Assert.Equal(2, buffer.Count);
        float[] positions = buffer.Snapshot().Positions;
        Assert.Equal(new[] { 2f, 0f, 0f, 3f, 0f, 0f }, positions);
    }

    [Fact]
    public void SnapshotKeepsIntensity()
    {
        var buffer = new LiveCloudBuffer(4);
        buffer.Add(new PointCloud([1f, 1f, 1f], [42]));

        Assert.Equal(new byte[] { 42 }, buffer.Snapshot().Intensity);
    }

    [Fact]
    public void ClearRemovesAllPoints()
    {
        var buffer = new LiveCloudBuffer(4);
        buffer.Add(Cloud(1f, 0f, 0f));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.Snapshot().Count);
    }

    [Fact]
    public void SnapshotOfEmptyBufferIsEmpty()
    {
        var buffer = new LiveCloudBuffer(4);

        Assert.Equal(0, buffer.Snapshot().Count);
    }

    [Fact]
    public void CapacityMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveCloudBuffer(0));
    }
}
