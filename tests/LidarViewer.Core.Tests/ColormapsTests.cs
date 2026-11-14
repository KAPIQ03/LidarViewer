using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class ColormapsTests
{
    [Fact]
    public void Build_ReturnsRequestedNumberOfColors()
    {
        var lut = Colormaps.Build(ColormapKind.Viridis, 64);

        Assert.Equal(64, lut.Length);
    }

    [Fact]
    public void Build_SizeBelowTwo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Colormaps.Build(ColormapKind.Jet, 1));
    }

    [Fact]
    public void Grayscale_StartsBlackAndEndsWhite()
    {
        var lut = Colormaps.Build(ColormapKind.Grayscale, 256);

        Assert.Equal(new Rgb(0, 0, 0), lut[0]);
        Assert.Equal(new Rgb(255, 255, 255), lut[^1]);
    }

    [Fact]
    public void Grayscale_IsMonotonicInBrightness()
    {
        var lut = Colormaps.Build(ColormapKind.Grayscale, 256);

        for (int i = 1; i < lut.Length; i++)
            Assert.True(lut[i].R >= lut[i - 1].R, $"przy indeksie {i}");
    }

    [Fact]
    public void Viridis_StartsDarkPurpleAndEndsYellow()
    {
        var lut = Colormaps.Build(ColormapKind.Viridis, 256);

        Assert.Equal(new Rgb(68, 1, 84), lut[0]);
        Assert.Equal(new Rgb(253, 231, 37), lut[^1]);
    }

    [Fact]
    public void Turbo_StartsDarkBlueAndEndsDarkRed()
    {
        var lut = Colormaps.Build(ColormapKind.Turbo, 256);

        Assert.True(lut[0].B > lut[0].R);
        Assert.True(lut[^1].R > lut[^1].B);
    }

    [Fact]
    public void Jet_StartsBlueAndEndsDarkRed()
    {
        var lut = Colormaps.Build(ColormapKind.Jet, 256);

        Assert.Equal(new Rgb(0, 0, 127), lut[0]);
        Assert.Equal(new Rgb(127, 0, 0), lut[^1]);
    }

    [Fact]
    public void All_ContainsEveryKind()
    {
        Assert.Equal(4, Colormaps.All.Count);
        Assert.Contains(ColormapKind.Grayscale, Colormaps.All);
        Assert.Contains(ColormapKind.Viridis, Colormaps.All);
        Assert.Contains(ColormapKind.Turbo, Colormaps.All);
        Assert.Contains(ColormapKind.Jet, Colormaps.All);
    }
}
