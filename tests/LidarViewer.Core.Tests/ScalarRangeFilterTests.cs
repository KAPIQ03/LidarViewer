using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class ScalarRangeFilterTests
{
    private static PointCloud Cloud() => new([0f, 0f, 0f, 0f, 0f, 10f, 0f, 0f, 20f], [1, 2, 3]);

    [Fact]
    public void KeepsOnlyPointsInsideRange()
    {
        var result = ScalarRangeFilter.Apply(Cloud(), ScalarKind.Height, 5f, 15f);

        Assert.Equal(1, result.Count);
        Assert.Equal(10f, result.Positions[2]);
    }

    [Fact]
    public void RangeIsInclusive()
    {
        var result = ScalarRangeFilter.Apply(Cloud(), ScalarKind.Height, 0f, 10f);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MissingAttributeYieldsEmptyCloud()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);

        var result = ScalarRangeFilter.Apply(cloud, ScalarKind.Intensity, 0f, 255f);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void InvertedRangeYieldsEmptyCloud()
    {
        var result = ScalarRangeFilter.Apply(Cloud(), ScalarKind.Height, 15f, 5f);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void PreservesIntensityAndOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 0f, 0f, 10f], [7, 8]) { Origin = (1, 2, 3) };

        var result = ScalarRangeFilter.Apply(cloud, ScalarKind.Height, 5f, 15f);

        Assert.Equal(8, result.Intensity![0]);
        Assert.Equal((1, 2, 3), result.Origin);
    }

    [Fact]
    public void SourceCloudIsNotModified()
    {
        var cloud = Cloud();

        ScalarRangeFilter.Apply(cloud, ScalarKind.Height, 5f, 15f);

        Assert.Equal(3, cloud.Count);
    }
}
