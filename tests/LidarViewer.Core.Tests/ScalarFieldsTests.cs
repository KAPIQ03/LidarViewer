using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class ScalarFieldsTests
{
    [Fact]
    public void GetAttribute_Intensity_ConvertsBytesToFloats()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f], [10, 250]);

        var attr = cloud.GetAttribute(ScalarKind.Intensity);

        Assert.NotNull(attr);
        Assert.Equal("intensity", attr.Name);
        Assert.Equal([10f, 250f], attr.Values);
        Assert.Equal(10f, attr.Min);
        Assert.Equal(250f, attr.Max);
    }

    [Fact]
    public void GetAttribute_Intensity_ReturnsNullWhenAbsent()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);

        Assert.Null(cloud.GetAttribute(ScalarKind.Intensity));
    }

    [Fact]
    public void GetAttribute_Distance_UsesOriginShiftedPositions()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 3f, 4f, 0f]) { Origin = (10, 0, 0) };

        var attr = cloud.GetAttribute(ScalarKind.Distance);

        Assert.NotNull(attr);
        Assert.Equal("distance", attr.Name);
        Assert.Equal(10f, attr.Values[0], 4);
        Assert.Equal(MathF.Sqrt(185f), attr.Values[1], 4);
    }

    [Fact]
    public void GetAttribute_Height_ReturnsZCoordinate()
    {
        var cloud = new PointCloud([1f, 2f, 3f, 4f, 5f, -6f]);

        var attr = cloud.GetAttribute(ScalarKind.Height);

        Assert.NotNull(attr);
        Assert.Equal("height", attr.Name);
        Assert.Equal([3f, -6f], attr.Values);
        Assert.Equal(-6f, attr.Min);
        Assert.Equal(3f, attr.Max);
    }

    [Fact]
    public void GetAttribute_Height_UsesWorldCoordinateIncludingOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 2f]) { Origin = (10, 20, 100) };

        var attr = cloud.GetAttribute(ScalarKind.Height);

        Assert.NotNull(attr);
        Assert.Equal(102f, attr.Values[0], 4);
    }

    [Fact]
    public void GetAttribute_ReturnsSameInstanceOnSecondCall()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 0f, 0f]);

        var first = cloud.GetAttribute(ScalarKind.Distance);
        var second = cloud.GetAttribute(ScalarKind.Distance);

        Assert.Same(first, second);
    }
}
