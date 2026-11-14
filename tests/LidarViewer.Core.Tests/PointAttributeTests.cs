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

    [Fact]
    public void Constructor_IgnoresInfinity()
    {
        var attr = new PointAttribute("x", [0f, float.PositiveInfinity, 10f]);

        Assert.Equal(0f, attr.Min);
        Assert.Equal(10f, attr.Max);
    }

    [Fact]
    public void Constructor_ExtremeValues_AreNotTreatedAsEmpty()
    {
        var attr = new PointAttribute("x", [float.MaxValue, 5f]);

        Assert.Equal(5f, attr.Min);
        Assert.Equal(float.MaxValue, attr.Max);

        var single = new PointAttribute("x", [float.MaxValue]);

        Assert.Equal(float.MaxValue, single.Min);
        Assert.Equal(float.MaxValue, single.Max);

        var lowest = new PointAttribute("x", [float.MinValue]);

        Assert.Equal(float.MinValue, lowest.Min);
        Assert.Equal(float.MinValue, lowest.Max);
    }

    [Fact]
    public void Constructor_NullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PointAttribute(null!, []));
    }
}
