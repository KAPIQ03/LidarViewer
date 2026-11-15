using System.Text;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class ExportWriterTests : TempDirTestBase
{
    private static PointCloud SampleCloud() =>
        new([1f, 2f, 3f, 4f, 5f, 6f], [10, 20]);

    [Fact]
    public void PlyWriter_WritesReadableHeader()
    {
        string path = Path.Combine(Dir, "a.ply");
        PlyWriter.Write(path, SampleCloud());

        string header = ReadHeader(path);

        Assert.Contains("ply", header);
        Assert.Contains("format binary_little_endian 1.0", header);
        Assert.Contains("element vertex 2", header);
        Assert.Contains("property float x", header);
        Assert.Contains("property float y", header);
        Assert.Contains("property float z", header);
        Assert.Contains("property uchar intensity", header);
        Assert.Contains("end_header", header);
    }

    [Fact]
    public void PlyWriter_RoundTripsThroughReader()
    {
        string path = Path.Combine(Dir, "a.ply");
        PlyWriter.Write(path, SampleCloud());

        PointCloud read = PointCloudLoader.Read(path);

        Assert.Equal(2, read.Count);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], read.Positions);
        Assert.Equal(new byte[] { 10, 20 }, read.Intensity);
    }

    [Fact]
    public void PlyWriter_WritesWorldCoordinates()
    {
        var cloud = new PointCloud([1f, 2f, 3f], [7]) { Origin = (100.5, 0, 0) };
        string path = Path.Combine(Dir, "a.ply");

        PlyWriter.Write(path, cloud);

        PointCloud read = PointCloudLoader.Read(path);
        Assert.Equal(101.5f, read.Positions[0], 3);
    }

    [Fact]
    public void PlyWriter_OmitsIntensityWhenCloudHasNone()
    {
        string path = Path.Combine(Dir, "a.ply");
        PlyWriter.Write(path, new PointCloud([1f, 2f, 3f]));

        Assert.DoesNotContain("intensity", ReadHeader(path));
    }

    [Fact]
    public void CsvWriter_WritesHeaderAndRows()
    {
        string path = Path.Combine(Dir, "a.csv");
        CsvWriter.Write(path, SampleCloud());

        string[] lines = File.ReadAllLines(path);

        Assert.Equal("x,y,z,intensity", lines[0]);
        Assert.Equal("1,2,3,10", lines[1]);
        Assert.Equal("4,5,6,20", lines[2]);
    }

    [Fact]
    public void CsvWriter_UsesInvariantFormatting()
    {
        string path = Path.Combine(Dir, "a.csv");
        CsvWriter.Write(path, new PointCloud([1.5f, 2.25f, 3f], [1]));

        Assert.Contains("1.5,2.25,3,1", File.ReadAllText(path));
    }

    [Fact]
    public void CsvWriter_RoundTripsThroughReader()
    {
        string path = Path.Combine(Dir, "a.csv");
        CsvWriter.Write(path, SampleCloud());

        PointCloud read = PointCloudLoader.Read(path);

        Assert.Equal(2, read.Count);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], read.Positions);
        Assert.Equal(new byte[] { 10, 20 }, read.Intensity);
    }

    [Fact]
    public void CsvWriter_OmitsIntensityColumnWhenCloudHasNone()
    {
        string path = Path.Combine(Dir, "a.csv");
        CsvWriter.Write(path, new PointCloud([1f, 2f, 3f]));

        string[] lines = File.ReadAllLines(path);
        Assert.Equal("x,y,z", lines[0]);
        Assert.Equal("1,2,3", lines[1]);
    }

    [Fact]
    public void PointCloudWriter_ChoosesWriterByExtension()
    {
        string ply = Path.Combine(Dir, "a.ply");
        string xyz = Path.Combine(Dir, "a.xyz");

        PointCloudWriter.Write(SampleCloud(), ply);
        PointCloudWriter.Write(SampleCloud(), xyz);

        Assert.Contains("binary_little_endian", ReadHeader(ply));
        Assert.StartsWith("x,y,z", File.ReadAllText(xyz));
    }

    [Fact]
    public void PointCloudWriter_UnknownExtension_ThrowsAndListsSupported()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => PointCloudWriter.Write(SampleCloud(), Path.Combine(Dir, "a.bogus")));

        Assert.Contains(".ply", ex.Message);
    }

    [Fact]
    public void PointCloudWriter_SupportedExtensions_CoverPlyAndText()
    {
        Assert.Contains(".ply", PointCloudWriter.SupportedExtensions);
        Assert.Contains(".csv", PointCloudWriter.SupportedExtensions);
        Assert.Contains(".xyz", PointCloudWriter.SupportedExtensions);
    }

    [Fact]
    public void Loader_ReadsExportedXyzFile()
    {
        string path = Path.Combine(Dir, "a.xyz");
        PointCloudWriter.Write(SampleCloud(), path);

        PointCloud read = PointCloudLoader.Read(path);

        Assert.Equal(2, read.Count);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], read.Positions);
    }

    private static string ReadHeader(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.ASCII.GetString(bytes);
        int end = text.IndexOf("end_header", StringComparison.Ordinal);
        return end < 0 ? text : text[..(end + "end_header".Length)];
    }
}
