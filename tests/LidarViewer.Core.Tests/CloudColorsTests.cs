using LidarViewer.Core.Models;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class CloudColorsTests
{
    [Fact]
    public void Build_SolidMode_FillsEveryPointWithSameColor()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f]);

        var rgba = CloudColors.Build(cloud, new ColorSettings { Mode = ColorMode.Solid, Solid = new Rgba(10, 20, 30, 40) });

        Assert.Equal(8, rgba.Length);
        Assert.Equal([(byte)10, (byte)20, (byte)30, (byte)40], rgba[..4]);
        Assert.Equal([(byte)10, (byte)20, (byte)30, (byte)40], rgba[4..]);
    }

    [Fact]
    public void Build_IntensityMode_MapsExtremesToRampEnds()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f], [0, 255]);

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Intensity,
            Map = ColormapKind.Grayscale,
            Min = 0f,
            Max = 255f,
        });

        Assert.Equal((byte)0, rgba[0]);
        Assert.Equal((byte)255, rgba[4]);
        Assert.Equal((byte)255, rgba[5]);
        Assert.Equal((byte)255, rgba[6]);
    }

    [Fact]
    public void Build_DistanceMode_UsesRangeFromOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 0f]) { Origin = (10, 0, 0) };

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Distance,
            Map = ColormapKind.Grayscale,
            Min = 0f,
            Max = 10f,
        });

        Assert.Equal((byte)255, rgba[0]);
    }

    [Fact]
    public void Build_MissingIntensity_FallsBackToGray()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f]);

        var rgba = CloudColors.Build(cloud, new ColorSettings { Mode = ColorMode.Intensity });

        Assert.Equal((byte)128, rgba[0]);
        Assert.Equal((byte)128, rgba[1]);
        Assert.Equal((byte)128, rgba[2]);
        Assert.Equal((byte)255, rgba[3]);
    }

    [Fact]
    public void Build_EmptyCloud_ReturnsEmptyBuffer()
    {
        var rgba = CloudColors.Build(new PointCloud([]), new ColorSettings());

        Assert.Empty(rgba);
    }

    [Fact]
    public void Build_IntensityShorterThanPointCount_DoesNotThrowAndFallsBackToGray()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f, 2f, 2f, 2f], [0]);

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Intensity,
            Map = ColormapKind.Grayscale,
            Min = 0f,
            Max = 255f,
        });

        Assert.Equal(12, rgba.Length);
        Assert.Equal((byte)0, rgba[0]);
        Assert.Equal((byte)128, rgba[4]);
        Assert.Equal((byte)128, rgba[8]);
    }

    [Fact]
    public void Build_RampMode_WritesRgbaInOrder()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f], [0, 255]);

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Intensity,
            Map = ColormapKind.Viridis,
            Min = 0f,
            Max = 255f,
        });

        Assert.Equal([(byte)68, (byte)1, (byte)84, (byte)255], rgba[..4]);
        Assert.Equal([(byte)253, (byte)231, (byte)37, (byte)255], rgba[4..]);
    }

    [Fact]
    public void Build_DegenerateRange_FillsCloudWithSingleColor()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f], [0, 255]);

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Intensity,
            Map = ColormapKind.Grayscale,
            Min = 5f,
            Max = 5f,
        });

        Assert.Equal([(byte)0, (byte)0, (byte)0, (byte)255], rgba[..4]);
        Assert.Equal([(byte)0, (byte)0, (byte)0, (byte)255], rgba[4..]);
    }

    [Fact]
    public void Build_FallbackGray_IsOpaque()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f], [0]);

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Intensity,
            Map = ColormapKind.Grayscale,
            Min = 0f,
            Max = 255f,
        });

        Assert.Equal([(byte)0, (byte)0, (byte)0, (byte)255], rgba[..4]);
        Assert.Equal([(byte)128, (byte)128, (byte)128, (byte)255], rgba[4..]);
    }
}
