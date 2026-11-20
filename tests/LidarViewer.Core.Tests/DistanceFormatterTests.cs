using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class DistanceFormatterTests
{
    [Theory]
    [InlineData(0.0, "0 m")]
    [InlineData(0.005, "5 mm")]
    [InlineData(0.5, "50 cm")]
    [InlineData(0.05, "5 cm")]
    [InlineData(1.0, "1 m")]
    [InlineData(12.345, "12,35 m")]
    [InlineData(2500.0, "2,5 km")]
    public void PicksReadableUnit(double meters, string expected)
    {
        Assert.Equal(expected, DistanceFormatter.Format(meters));
    }

    [Fact]
    public void InvalidValueIsShownAsDash()
    {
        Assert.Equal("—", DistanceFormatter.Format(double.NaN));
    }
}
