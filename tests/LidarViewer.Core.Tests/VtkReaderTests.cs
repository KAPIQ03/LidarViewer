using System.Buffers.Binary;
using System.Text;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class VtkReaderTests : TempDirTestBase
{
    [Fact]
    public void Read_Ascii_PointsAndPointDataAfterCellData()
    {
        string text = """
            # vtk DataFile Version 3.0
            test
            ASCII
            DATASET POLYDATA
            POINTS 2 float
            1 2 3 4 5 6
            VERTICES 2 4
            1 0 1 1
            CELL_DATA 2
            SCALARS cellval float 1
            LOOKUP_TABLE default
            0.5 0.7
            POINT_DATA 2
            SCALARS intensity unsigned_char 1
            LOOKUP_TABLE default
            10 20
            SCALARS category int
            LOOKUP_TABLE default
            7 8
            """;

        var cloud = VtkReader.Read(Write("a.vtk", text));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
        Assert.Equal(new[] { 7, 8 }, cloud.Category);
    }

    [Fact]
    public void Read_BinaryBigEndian()
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("# vtk DataFile Version 3.0\ntest\nBINARY\nDATASET POLYDATA\nPOINTS 2 float\n"));
        Span<byte> b = stackalloc byte[4];
        foreach (float v in new[] { 1f, 2f, 3f, 4f, 5f, 6f })
        {
            BinaryPrimitives.WriteSingleBigEndian(b, v);
            ms.Write(b);
        }
        ms.Write(Encoding.ASCII.GetBytes("\nVERTICES 2 4\n"));
        foreach (int v in new[] { 1, 0, 1, 1 })
        {
            BinaryPrimitives.WriteInt32BigEndian(b, v);
            ms.Write(b);
        }
        ms.Write(Encoding.ASCII.GetBytes("\nPOINT_DATA 2\nSCALARS intensity unsigned_char 1\nLOOKUP_TABLE default\n"));
        ms.Write([10, 20]);
        ms.Write("\n"u8);

        var cloud = VtkReader.Read(Write("a.vtk", ms.ToArray()));

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Fact]
    public void Read_DatasetWithoutPoints_IsNotSupported() =>
        Assert.Throws<NotSupportedException>(() => VtkReader.Read(Write("a.vtk",
            "# vtk DataFile Version 3.0\nt\nASCII\nDATASET STRUCTURED_POINTS\nDIMENSIONS 2 2 2\n")));

    [Fact]
    public void Read_NotVtk_Throws() =>
        Assert.Throws<InvalidDataException>(() => VtkReader.Read(Write("a.vtk", "hello\nworld\n")));

    [Fact]
    public void Read_TruncatedBinary_Throws()
    {
        var bytes = Encoding.ASCII.GetBytes("# vtk DataFile Version 3.0\nt\nBINARY\nDATASET POLYDATA\nPOINTS 2 float\n")
            .Concat(new byte[8]).ToArray();
        Assert.Throws<EndOfStreamException>(() => VtkReader.Read(Write("a.vtk", bytes)));
    }
}
