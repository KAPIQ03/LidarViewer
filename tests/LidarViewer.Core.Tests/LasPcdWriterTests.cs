using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class LasPcdWriterTests : TempDirTestBase
{
    private static PointCloud Sample() =>
        new([1f, 2f, 3f, 4f, 5f, 6f, -1f, 0.5f, 2.25f], [10, 20, 30], [2, 5, 1]) { Origin = (1000, 2000, 30) };

    private static (double X, double Y, double Z) World(PointCloud cloud, int i) =>
        (cloud.Positions[i * 3] + cloud.Origin.X, cloud.Positions[i * 3 + 1] + cloud.Origin.Y, cloud.Positions[i * 3 + 2] + cloud.Origin.Z);

    [Fact]
    public void LasRoundTripKeepsWorldCoordinatesIntensityAndClass()
    {
        string path = Path.Combine(Dir, "a.las");
        var source = Sample();

        LasWriter.Write(path, source);
        var read = PointCloudLoader.Read(path);

        Assert.Equal(3, read.Count);
        for (int i = 0; i < 3; i++)
        {
            var expected = World(source, i);
            var actual = World(read, i);
            Assert.Equal(expected.X, actual.X, 2);
            Assert.Equal(expected.Y, actual.Y, 2);
            Assert.Equal(expected.Z, actual.Z, 2);
        }

        Assert.Equal([10, 20, 30], read.Intensity);
        Assert.Equal([2, 5, 1], read.Category);
    }

    [Fact]
    public void LasHeaderHasSignatureAndPointCount()
    {
        string path = Path.Combine(Dir, "b.las");
        LasWriter.Write(path, Sample());

        byte[] bytes = File.ReadAllBytes(path);

        Assert.Equal("LASF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(3u, BitConverter.ToUInt32(bytes, 107));
        Assert.Equal(227 + 3 * 20, bytes.Length);
    }

    [Fact]
    public void EmptyCloudWritesValidLas()
    {
        string path = Path.Combine(Dir, "c.las");

        LasWriter.Write(path, new PointCloud([]));

        Assert.Equal(0, PointCloudLoader.Read(path).Count);
    }

    [Fact]
    public void PcdRoundTripKeepsWorldCoordinatesAndIntensity()
    {
        string path = Path.Combine(Dir, "a.pcd");
        var source = new PointCloud([1f, 2f, 3f, 4f, 5f, 6f], [10, 200]) { Origin = (100, 0, 0) };

        PcdWriter.Write(path, source);
        var read = PointCloudLoader.Read(path);

        Assert.Equal(2, read.Count);
        Assert.Equal([101f, 2f, 3f, 104f, 5f, 6f], read.Positions);
        Assert.Equal([10, 200], read.Intensity);
    }

    [Fact]
    public void PcdWithoutIntensityHasOnlyXyz()
    {
        string path = Path.Combine(Dir, "b.pcd");

        PcdWriter.Write(path, new PointCloud([1f, 2f, 3f]));
        var read = PointCloudLoader.Read(path);

        Assert.Null(read.Intensity);
        Assert.Equal(1, read.Count);
        Assert.Contains("FIELDS x y z\n", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData(".las")]
    [InlineData(".pcd")]
    public void WriterRegistryHandlesNewExtensions(string extension)
    {
        string path = Path.Combine(Dir, "r" + extension);

        PointCloudWriter.Write(Sample(), path);

        Assert.True(File.Exists(path));
        Assert.Contains(extension, PointCloudWriter.SupportedExtensions);
    }
}
