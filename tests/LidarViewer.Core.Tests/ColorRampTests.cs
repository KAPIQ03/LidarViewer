using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class ColorRampTests
{
    private static readonly Rgb First = new(0, 0, 0);
    private static readonly Rgb Last = new(255, 255, 255);

    [Fact]
    public void ColorAt_ValueBelowMin_ClampsToFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(First, ramp.ColorAt(0f));
    }

    [Fact]
    public void ColorAt_ValueAboveMax_ClampsToLastColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(Last, ramp.ColorAt(1000f));
    }

    [Fact]
    public void ColorAt_ValueAtMin_GivesFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(First, ramp.ColorAt(10f));
    }

    [Fact]
    public void ColorAt_ValueAtMax_GivesLastColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(Last, ramp.ColorAt(20f));
    }

    [Fact]
    public void ColorAt_Midpoint_GivesMiddleBrightness()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 0f, 100f);

        var mid = ramp.ColorAt(50f);

        Assert.InRange(mid.R, 126, 130);
    }

    [Fact]
    public void ColorAt_EqualMinAndMax_ReturnsFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 5f, 5f);

        Assert.Equal(First, ramp.ColorAt(5f));
    }

    [Fact]
    public void ColorAt_NaN_ReturnsFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 0f, 10f);

        Assert.Equal(First, ramp.ColorAt(float.NaN));
    }
}
