using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;

namespace LidarViewer.Core.Tests.Playback;

public class CloudMergeTests
{
    [Fact]
    public void ConcatenatesPositionsAndAttributes()
    {
        var a = new PointCloud([1, 2, 3], [10]) { LaserId = [1] };
        var b = new PointCloud([4, 5, 6, 7, 8, 9], [20, 30]) { LaserId = [2, 3] };

        var merged = CloudMerge.Concatenate([a, b]);

        Assert.Equal(3, merged.Count);
        Assert.Equal([1f, 2, 3, 4, 5, 6, 7, 8, 9], merged.Positions);
        Assert.Equal([10, 20, 30], merged.Intensity);
        Assert.Equal([1, 2, 3], merged.LaserId);
    }

    [Fact]
    public void MissingAttributeInSomeCloudsIsZeroFilled()
    {
        var a = new PointCloud([1, 2, 3]);
        var b = new PointCloud([4, 5, 6], [99]);

        var merged = CloudMerge.Concatenate([a, b]);

        Assert.Equal([0, 99], merged.Intensity);
        Assert.Null(merged.LaserId);
    }

    [Fact]
    public void SingleCloudIsReturnedAsIs()
    {
        var a = new PointCloud([1, 2, 3]);

        Assert.Same(a, CloudMerge.Concatenate([a]));
    }

    [Fact]
    public void EmptyListGivesEmptyCloud()
    {
        Assert.Equal(0, CloudMerge.Concatenate([]).Count);
    }
}
