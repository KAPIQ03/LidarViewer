using System.Text;
using LidarViewer.Core.Vtp;

namespace LidarViewer.Core.Tests;

public class VtpReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vtp-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteSample(string? rootAttrs = null, string appendedEncoding = "raw", string pointsFormat = "appended")
    {
        float[] pts = [1f, 2f, 3f, 4f, 5f, 6f, -7f, 8f, 9f];
        byte[] intensity = [10, 20, 30];
        int[] category = [1, 2, 3];

        using var data = new MemoryStream();
        var w = new BinaryWriter(data);
        long catOffset = 0;
        w.Write((ulong)12); foreach (var c in category) w.Write(c);
        long intOffset = data.Position;
        w.Write((ulong)3); w.Write(intensity);
        long ptsOffset = data.Position;
        w.Write((ulong)36); foreach (var p in pts) w.Write(p);

        string xml = $"""
            <?xml version="1.0"?>
            <VTKFile type="PolyData" version="1.0" byte_order="LittleEndian" header_type="UInt64" {rootAttrs}>
              <PolyData>
                <Piece NumberOfPoints="3" NumberOfVerts="3">
                  <PointData>
                    <DataArray type="Int32" Name="category" format="appended" offset="{catOffset}"/>
                    <DataArray type="UInt8" Name="intensity" format="appended" offset="{intOffset}"/>
                  </PointData>
                  <Points>
                    <DataArray type="Float32" Name="Points" NumberOfComponents="3" format="{pointsFormat}" offset="{ptsOffset}"/>
                  </Points>
                </Piece>
              </PolyData>
              <AppendedData encoding="{appendedEncoding}">
            """;

        string path = Path.Combine(_dir, Guid.NewGuid() + ".vtp");
        using var f = File.Create(path);
        f.Write(Encoding.UTF8.GetBytes(xml + "\n_"));
        f.Write(data.ToArray());
        f.Write(Encoding.UTF8.GetBytes("\n  </AppendedData>\n</VTKFile>\n"));
        return path;
    }

    [Fact]
    public void Read_ReturnsPointsAndAttributes()
    {
        var cloud = VtpReader.Read(WriteSample());

        Assert.Equal(3, cloud.Count);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f, -7f, 8f, 9f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20, 30 }, cloud.Intensity);
        Assert.Equal(new[] { 1, 2, 3 }, cloud.Category);
    }

    [Fact]
    public void Read_ComputesBounds()
    {
        var cloud = VtpReader.Read(WriteSample());

        Assert.Equal(new System.Numerics.Vector3(-7, 2, 3), cloud.BoundsMin);
        Assert.Equal(new System.Numerics.Vector3(4, 8, 9), cloud.BoundsMax);
    }

    [Fact]
    public void Read_CompressedFile_Throws() =>
        Assert.Throws<NotSupportedException>(() =>
            VtpReader.Read(WriteSample(rootAttrs: "compressor=\"vtkZLibDataCompressor\"")));

    [Fact]
    public void Read_Base64Appended_Throws() =>
        Assert.Throws<NotSupportedException>(() => VtpReader.Read(WriteSample(appendedEncoding: "base64")));

    [Fact]
    public void Read_AsciiArray_Throws() =>
        Assert.Throws<NotSupportedException>(() => VtpReader.Read(WriteSample(pointsFormat: "ascii")));

    [Fact]
    public void Read_TruncatedFile_Throws()
    {
        string path = WriteSample();
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length - 60)]);

        Assert.Throws<EndOfStreamException>(() => VtpReader.Read(path));
    }

    [Fact]
    public void Read_RealFile_WhenPresent()
    {
        string path = "/Users/kacper/Downloads/pointCloud.vtp";
        if (!File.Exists(path)) return;

        var cloud = VtpReader.Read(path);

        Assert.Equal(345277, cloud.Count);
        Assert.Equal(345277, cloud.Intensity?.Length);
    }
}
