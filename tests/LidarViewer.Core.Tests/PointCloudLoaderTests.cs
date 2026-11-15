using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class PointCloudLoaderTests : TempDirTestBase
{
    [Fact]
    public void Read_ChoosesReaderByExtension_CaseInsensitive()
    {
        var cloud = PointCloudLoader.Read(Write("DATA.CSV", "1,2,3\n"));

        Assert.Equal(1, cloud.Count);
    }

    [Fact]
    public void Read_UnknownExtension_ThrowsAndListsSupported()
    {
        var ex = Assert.Throws<NotSupportedException>(() => PointCloudLoader.Read(Write("a.xyz2", "1 2 3")));
        Assert.Contains(".vtp", ex.Message);
    }

    [Fact]
    public void Read_DropsNonFinitePoints()
    {
        var cloud = PointCloudLoader.Read(Write("a.csv", "x,y,z,intensity\n1,2,3,10\nnan,2,3,20\n4,5,6,30\n7,inf,9,40\n"));

        Assert.Equal(2, cloud.Count);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 30 }, cloud.Intensity);
        Assert.Equal(new System.Numerics.Vector3(4, 5, 6), cloud.BoundsMax);
    }

    [Fact]
    public void SupportedExtensions_ContainsAllFormats()
    {
        foreach (var ext in new[] { ".vtp", ".vtk", ".csv", ".ply", ".pcd", ".las" })
            Assert.Contains(ext, PointCloudLoader.SupportedExtensions);
    }
}
