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

    [Fact]
    public void ColorAt_PositiveInfinity_ReturnsLastColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(Last, ramp.ColorAt(float.PositiveInfinity));
    }

    [Fact]
    public void ColorAt_NegativeInfinity_ReturnsFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(First, ramp.ColorAt(float.NegativeInfinity));
    }

    [Fact]
    public void ColorAt_SwappedRange_ReturnsFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 20f, 10f);

        Assert.Equal(First, ramp.ColorAt(15f));
    }

    [Fact]
    public void ColorAt_NonGrayscaleMap_UsesMapEndpoints()
    {
        var ramp = new ColorRamp(ColormapKind.Viridis, 0f, 1f);

        Assert.Equal(new Rgb(68, 1, 84), ramp.ColorAt(0f));
        Assert.Equal(new Rgb(253, 231, 37), ramp.ColorAt(1f));
    }

    [Fact]
    public void ColorAt_ExtremeRange_DoesNotOverflow()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, -float.MaxValue, float.MaxValue);

        Assert.Equal(First, ramp.ColorAt(-float.MaxValue));
        Assert.Equal(Last, ramp.ColorAt(float.MaxValue));
    }

    [Fact]
    public void IsDegenerate_TrueOnlyWhenRangeCollapsed()
    {
        Assert.True(new ColorRamp(ColormapKind.Grayscale, 5f, 5f).IsDegenerate);
        Assert.True(new ColorRamp(ColormapKind.Grayscale, 20f, 10f).IsDegenerate);
        Assert.False(new ColorRamp(ColormapKind.Grayscale, 10f, 20f).IsDegenerate);
    }

    [Fact]
    public void Settings_IsDegenerate_WhenMaxNotGreaterThanMin()
    {
        Assert.False(ColorSettings.Default.IsDegenerate);
        Assert.True(new ColorSettings { Min = 5f, Max = 5f }.IsDegenerate);
        Assert.True(new ColorSettings { Min = 10f, Max = 5f }.IsDegenerate);
    }
}
