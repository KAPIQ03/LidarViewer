using System.Numerics;
using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class SphereCropFilterTests
{
    [Fact]
    public void KeepsPointInsideSphere()
    {
        var cloud = new PointCloud([1f, 0f, 0f, 10f, 0f, 0f]);

        var result = SphereCropFilter.Apply(cloud, Vector3.Zero, 5f);

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public void BoundaryIsInclusive()
    {
        var cloud = new PointCloud([5f, 0f, 0f]);

        var result = SphereCropFilter.Apply(cloud, Vector3.Zero, 5f);

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public void RemovesPointOutsideSphere()
    {
        var cloud = new PointCloud([5.01f, 0f, 0f]);

        var result = SphereCropFilter.Apply(cloud, Vector3.Zero, 5f);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void NegativeRadiusThrows()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);

        Assert.Throws<ArgumentOutOfRangeException>(() => SphereCropFilter.Apply(cloud, Vector3.Zero, -1f));
    }

    [Fact]
    public void PreservesIntensityAndOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 10f, 0f, 0f], [3, 4]) { Origin = (1, 2, 3) };

        var result = SphereCropFilter.Apply(cloud, Vector3.Zero, 5f);

        Assert.Equal(3, result.Intensity![0]);
        Assert.Equal((1, 2, 3), result.Origin);
    }
}
