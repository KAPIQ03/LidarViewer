using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class PointAttributeTests
{
    [Fact]
    public void Constructor_ComputesMinAndMax()
    {
        var attr = new PointAttribute("intensity", [5f, 1f, 9f]);

        Assert.Equal("intensity", attr.Name);
        Assert.Equal(1f, attr.Min);
        Assert.Equal(9f, attr.Max);
    }

    [Fact]
    public void Constructor_IgnoresNaN()
    {
        var attr = new PointAttribute("distance", [float.NaN, 2f, 4f]);

        Assert.Equal(2f, attr.Min);
        Assert.Equal(4f, attr.Max);
    }

    [Fact]
    public void Constructor_EmptyArray_GivesZeroRange()
    {
        var attr = new PointAttribute("height", []);

        Assert.Equal(0f, attr.Min);
        Assert.Equal(0f, attr.Max);
    }

    [Fact]
    public void Constructor_NullValues_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PointAttribute("x", null!));
    }
}
