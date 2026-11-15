using System.Buffers.Binary;
using System.Text;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class PcdReaderTests : TempDirTestBase
{
    private static string Header(string data) =>
        "# .PCD v0.7\nVERSION 0.7\nFIELDS x y z intensity\nSIZE 4 4 4 4\nTYPE F F F F\nCOUNT 1 1 1 1\n" +
        $"WIDTH 2\nHEIGHT 1\nVIEWPOINT 0 0 0 1 0 0 0\nPOINTS 2\nDATA {data}\n";

    private static readonly float[] Values = [1f, 2f, 3f, 10f, 4f, 5f, 6f, 20f];

    [Fact]
    public void Read_Ascii()
    {
        var cloud = PcdReader.Read(Write("a.pcd", Header("ascii") + "1 2 3 10\n4 5 6 20\n"));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Fact]
    public void Read_Binary()
    {
        var bytes = Encoding.ASCII.GetBytes(Header("binary")).Concat(ToBytes(Values)).ToArray();

        var cloud = PcdReader.Read(Write("a.pcd", bytes));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Fact]
    public void Read_BinaryCompressed_ColumnMajorLzf()
    {
        float[] columnar = [1f, 4f, 2f, 5f, 3f, 6f, 10f, 20f];
        byte[] raw = ToBytes(columnar);
        var packed = new List<byte>();
        for (int i = 0; i < raw.Length; i += 32)
        {
            int run = Math.Min(32, raw.Length - i);
            packed.Add((byte)(run - 1));
            packed.AddRange(raw.AsSpan(i, run).ToArray());
        }
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes((uint)packed.Count));
        body.AddRange(BitConverter.GetBytes((uint)raw.Length));
        body.AddRange(packed);
        var bytes = Encoding.ASCII.GetBytes(Header("binary_compressed")).Concat(body).ToArray();

        var cloud = PcdReader.Read(Write("a.pcd", bytes));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Fact]
    public void Read_NoIntensityAndNoCount_Works()
    {
        string text = "FIELDS x y z\nSIZE 4 4 4\nTYPE F F F\nWIDTH 1\nHEIGHT 1\nDATA ascii\n7 8 9\n";

        var cloud = PcdReader.Read(Write("a.pcd", text));

        Assert.Equal([7f, 8f, 9f], cloud.Positions);
        Assert.Null(cloud.Intensity);
    }

    [Fact]
    public void Read_UnknownDataMode_Throws() =>
        Assert.Throws<NotSupportedException>(() => PcdReader.Read(Write("a.pcd", Header("weird"))));

    [Fact]
    public void Read_MissingXyz_Throws() =>
        Assert.Throws<InvalidDataException>(() =>
            PcdReader.Read(Write("a.pcd", "FIELDS a b\nSIZE 4 4\nTYPE F F\nPOINTS 1\nDATA ascii\n1 2\n")));

    [Fact]
    public void Lzf_BackReferenceWithOverlap()
    {
        byte[] packed = [0x00, (byte)'A', 0x40, 0x00];

        Assert.Equal("AAAAA"u8.ToArray(), Lzf.Decompress(packed, 5));
    }

    [Fact]
    public void Lzf_WrongOutputLength_Throws() =>
        Assert.Throws<InvalidDataException>(() => Lzf.Decompress([0x00, (byte)'A'], 3));

    private static byte[] ToBytes(float[] values)
    {
        var b = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(i * 4), values[i]);
        return b;
    }
}
