using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class StatisticalOutlierFilterTests
{
    private static PointCloud SquareWithOutlier()
    {
        var positions = new List<float>();
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                positions.AddRange([x, y, 0f]);

        positions.AddRange([100f, 0f, 0f]);
        return new PointCloud([.. positions]);
    }

    private static PointCloud UniformCloud(int count)
    {
        var positions = new float[count * 3];
        for (int i = 0; i < count; i++)
        {
            positions[i * 3] = i % 10;
            positions[i * 3 + 1] = (i / 10) % 10;
            positions[i * 3 + 2] = 0f;
        }
        return new PointCloud(positions);
    }

    [Fact]
    public void RemovesIsolatedPoint()
    {
        var cloud = SquareWithOutlier();

        var result = StatisticalOutlierFilter.Apply(cloud, meanK: 4, stdDevThreshold: 1.5f);

        Assert.Equal(9, result.Count);
    }

    [Fact]
    public void KeepsUniformCloud()
    {
        var cloud = UniformCloud(100);

        var result = StatisticalOutlierFilter.Apply(cloud, meanK: 4, stdDevThreshold: 1.5f);

        Assert.Equal(100, result.Count);
    }

    [Fact]
    public void MeanKBelowOneThrows()
    {
        var cloud = UniformCloud(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => StatisticalOutlierFilter.Apply(cloud, 0, 1.5f));
    }

    [Fact]
    public void NegativeThresholdThrows()
    {
        var cloud = UniformCloud(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => StatisticalOutlierFilter.Apply(cloud, 2, -1f));
    }

    [Fact]
    public void CloudSmallerThanMeanKIsKeptWhole()
    {
        var cloud = UniformCloud(3);

        var result = StatisticalOutlierFilter.Apply(cloud, meanK: 10, stdDevThreshold: 1.5f);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void PreservesIntensityAndOrigin()
    {
        var positions = new List<float>();
        var intensity = new List<byte>();
        for (int i = 0; i < 20; i++)
        {
            positions.AddRange([i % 4, i / 4, 0f]);
            intensity.Add((byte)(i + 1));
        }

        var cloud = new PointCloud([.. positions], [.. intensity]) { Origin = (1, 2, 3) };

        var result = StatisticalOutlierFilter.Apply(cloud, meanK: 4, stdDevThreshold: 3f);

        Assert.Equal(20, result.Count);
        Assert.Equal((1, 2, 3), result.Origin);
        Assert.Equal(20, result.Intensity!.Length);
    }
}
