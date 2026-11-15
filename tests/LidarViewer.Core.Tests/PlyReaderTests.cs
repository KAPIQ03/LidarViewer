using System.Buffers.Binary;
using System.Text;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class PlyReaderTests : TempDirTestBase
{
    private const string AsciiBody = "1 2 3 10\n4 5 6 20\n";

    private static string Header(string format, string vertexProps = "float x\nproperty float y\nproperty float z\nproperty uchar intensity") =>
        $"ply\nformat {format} 1.0\ncomment test\nelement vertex 2\nproperty {vertexProps}\n" +
        "element face 1\nproperty list uchar int vertex_indices\nend_header\n";

    [Fact]
    public void Read_Ascii()
    {
        var cloud = PlyReader.Read(Write("a.ply", Header("ascii") + AsciiBody + "3 0 1 0\n"));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_Binary_LittleAndBigEndian(bool bigEndian)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(Header(bigEndian ? "binary_big_endian" : "binary_little_endian")));
        Span<byte> f = stackalloc byte[4];
        foreach (var (x, y, z, i) in new[] { (1f, 2f, 3f, (byte)10), (4f, 5f, 6f, (byte)20) })
        {
            foreach (float v in new[] { x, y, z })
            {
                if (bigEndian) BinaryPrimitives.WriteSingleBigEndian(f, v); else BinaryPrimitives.WriteSingleLittleEndian(f, v);
                ms.Write(f);
            }
            ms.WriteByte(i);
        }
        ms.Write([3, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0]);

        var cloud = PlyReader.Read(Write("a.ply", ms.ToArray()));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Fact]
    public void Read_MissingZ_Throws() =>
        Assert.Throws<InvalidDataException>(() =>
            PlyReader.Read(Write("a.ply", "ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nproperty float y\nend_header\n1 2\n")));

    [Fact]
    public void Read_NotPly_Throws() =>
        Assert.Throws<InvalidDataException>(() => PlyReader.Read(Write("a.ply", "hello\n")));

    [Fact]
    public void Read_TruncatedBinary_Throws()
    {
        var bytes = Encoding.ASCII.GetBytes(Header("binary_little_endian")).Concat(new byte[10]).ToArray();
        Assert.Throws<EndOfStreamException>(() => PlyReader.Read(Write("a.ply", bytes)));
    }
}
