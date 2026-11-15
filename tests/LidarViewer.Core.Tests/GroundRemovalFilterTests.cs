using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class GroundRemovalFilterTests
{
    private static PointCloud GroundAndUpper(int ground, int upper, float gap)
    {
        var positions = new List<float>();
        for (int i = 0; i < ground; i++)
            positions.AddRange([(i % 10) * 0.5f, (i / 10) * 0.5f, 0f]);

        for (int i = 0; i < upper; i++)
            positions.AddRange([(i % 10) * 0.5f + 0.25f, (i / 10) * 0.5f + 0.25f, gap]);

        return new PointCloud([.. positions]);
    }

    [Fact]
    public void RemovesPointsOnGroundPlane()
    {
        var cloud = GroundAndUpper(ground: 100, upper: 50, gap: 10f);

        var result = GroundRemovalFilter.Apply(cloud, distanceThreshold: 0.5f, iterations: 50);

        Assert.Equal(50, result.Count);
    }

    [Fact]
    public void KeepsPointsAboveGround()
    {
        var cloud = GroundAndUpper(ground: 60, upper: 40, gap: 3f);

        var result = GroundRemovalFilter.Apply(cloud, distanceThreshold: 0.5f, iterations: 100);

        Assert.Equal(40, result.Count);
    }

    [Fact]
    public void RemovesTiltedGroundPlane()
    {
        var positions = new List<float>();
        for (int i = 0; i < 100; i++)
        {
            float x = (i % 10) * 0.5f;
            float y = (i / 10) * 0.5f;
            positions.AddRange([x, y, 0.1f * x]);
        }

        for (int i = 0; i < 30; i++)
            positions.AddRange([(i % 10) * 0.5f, (i / 10) * 0.5f, 5f]);

        var cloud = new PointCloud([.. positions]);

        var result = GroundRemovalFilter.Apply(cloud, distanceThreshold: 0.1f, iterations: 200);

        Assert.Equal(30, result.Count);
    }

    [Fact]
    public void IterationsBelowOneThrows()
    {
        var cloud = GroundAndUpper(ground: 10, upper: 10, gap: 5f);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GroundRemovalFilter.Apply(cloud, 0.5f, 0));
    }

    [Fact]
    public void NegativeThresholdThrows()
    {
        var cloud = GroundAndUpper(ground: 10, upper: 10, gap: 5f);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GroundRemovalFilter.Apply(cloud, -1f, 10));
    }

    [Fact]
    public void EmptyCloudStaysEmpty()
    {
        var cloud = new PointCloud([]);

        var result = GroundRemovalFilter.Apply(cloud, 0.5f, 10);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void CloudWithTooFewPointsIsKeptWhole()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f]);

        var result = GroundRemovalFilter.Apply(cloud, 0.5f, 10);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SameInputGivesSameResult()
    {
        var cloud = GroundAndUpper(ground: 60, upper: 60, gap: 4f);

        var first = GroundRemovalFilter.Apply(cloud, 0.5f, 50);
        var second = GroundRemovalFilter.Apply(cloud, 0.5f, 50);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Positions, second.Positions);
    }

    [Fact]
    public void PreservesIntensityAndOrigin()
    {
        var positions = new List<float>();
        var intensity = new List<byte>();
        for (int i = 0; i < 60; i++)
        {
            positions.AddRange([(i % 10) * 0.5f, (i / 10) * 0.5f, 0f]);
            intensity.Add((byte)(i + 1));
        }

        for (int i = 0; i < 20; i++)
        {
            positions.AddRange([(i % 10) * 0.5f, (i / 10) * 0.5f, 6f]);
            intensity.Add((byte)(i + 1));
        }

        var cloud = new PointCloud([.. positions], [.. intensity]) { Origin = (1, 2, 3) };

        var result = GroundRemovalFilter.Apply(cloud, 0.5f, 50);

        Assert.Equal(20, result.Count);
        Assert.Equal((1, 2, 3), result.Origin);
        Assert.Equal(20, result.Intensity!.Length);
    }
}
