using System.Numerics;
using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class BoxCropFilterTests
{
    private static PointCloud Cloud() => new(
    [
        0f, 0f, 0f,
        5f, 5f, 5f,
        -6f, 0f, 0f,
        0f, 6f, 0f,
        0f, 0f, 6f,
    ]);

    [Fact]
    public void KeepsPointInsideBox()
    {
        var result = BoxCropFilter.Apply(Cloud(), new Vector3(-1), new Vector3(1));

        Assert.Equal(1, result.Count);
        Assert.Equal(0f, result.Positions[0]);
    }

    [Fact]
    public void RemovesPointOutsideEachAxis()
    {
        var result = BoxCropFilter.Apply(Cloud(), new Vector3(-1), new Vector3(1));

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public void BoundaryIsInclusive()
    {
        var cloud = new PointCloud([1f, 1f, 1f]);

        var result = BoxCropFilter.Apply(cloud, new Vector3(-1), new Vector3(1));

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public void InvertedBoxYieldsEmptyCloud()
    {
        var result = BoxCropFilter.Apply(Cloud(), new Vector3(1), new Vector3(-1));

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void PreservesIntensityCategoryAndOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 5f, 5f, 5f], [3, 4], [7, 8]) { Origin = (1, 2, 3) };

        var result = BoxCropFilter.Apply(cloud, new Vector3(-1), new Vector3(1));

        Assert.Equal(3, result.Intensity![0]);
        Assert.Equal(7, result.Category![0]);
        Assert.Equal((1, 2, 3), result.Origin);
    }
}
