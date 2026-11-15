using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class VoxelGridFilterTests
{
    [Fact]
    public void MergesPointsInsideOneVoxel()
    {
        var cloud = new PointCloud([0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.2f]);

        var result = VoxelGridFilter.Apply(cloud, 1f);

        Assert.Equal(1, result.Count);
        Assert.Equal(0.15f, result.Positions[0], 5);
        Assert.Equal(0.15f, result.Positions[1], 5);
        Assert.Equal(0.15f, result.Positions[2], 5);
    }

    [Fact]
    public void KeepsPointsInDifferentVoxels()
    {
        var cloud = new PointCloud([0.5f, 0f, 0f, 1.5f, 0f, 0f]);

        var result = VoxelGridFilter.Apply(cloud, 1f);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void PreservesIntensityOfFirstPointInVoxel()
    {
        var cloud = new PointCloud([0.1f, 0f, 0f, 0.2f, 0f, 0f], [7, 9]);

        var result = VoxelGridFilter.Apply(cloud, 1f);

        Assert.Equal(1, result.Count);
        Assert.Equal(7, result.Intensity![0]);
    }

    [Fact]
    public void NonPositiveVoxelSizeThrows()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);

        Assert.Throws<ArgumentOutOfRangeException>(() => VoxelGridFilter.Apply(cloud, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => VoxelGridFilter.Apply(cloud, -1f));
    }

    [Fact]
    public void PreservesOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 0f]) { Origin = (1, 2, 3) };

        var result = VoxelGridFilter.Apply(cloud, 1f);

        Assert.Equal((1, 2, 3), result.Origin);
    }
}
