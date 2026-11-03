using System.Buffers.Binary;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class LasReaderTests : TempDirTestBase
{
    private record Pt(int X, int Y, int Z, ushort Intensity, byte Class);

    /// <summary>Buduje plik LAS: nagłówek (227 B dla 1.2, 375 B dla 1.4) + rekordy zadanego formatu.</summary>
    private byte[] Build(int minor, byte rawFormat, int recordLength, Pt[] points)
    {
        int headerSize = minor >= 4 ? 375 : 227;
        var h = new byte[headerSize];
        "LASF"u8.CopyTo(h);
        h[24] = 1; h[25] = (byte)minor;
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(94), (ushort)headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(96), (uint)headerSize);
        h[104] = rawFormat;
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(105), (ushort)recordLength);
        if (minor >= 4) BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(247), (ulong)points.Length);
        else BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(107), (uint)points.Length);
        foreach (int o in new[] { 131, 139, 147 }) BinaryPrimitives.WriteDoubleLittleEndian(h.AsSpan(o), 0.01);
        BinaryPrimitives.WriteDoubleLittleEndian(h.AsSpan(155), 500000);
        BinaryPrimitives.WriteDoubleLittleEndian(h.AsSpan(163), 6000000);
        BinaryPrimitives.WriteDoubleLittleEndian(h.AsSpan(171), 100);

        int classOffset = (rawFormat & 0x3F) <= 5 ? 15 : 16;
        var body = new byte[points.Length * recordLength];
        for (int i = 0; i < points.Length; i++)
        {
            var rec = body.AsSpan(i * recordLength);
            BinaryPrimitives.WriteInt32LittleEndian(rec, points[i].X);
            BinaryPrimitives.WriteInt32LittleEndian(rec[4..], points[i].Y);
            BinaryPrimitives.WriteInt32LittleEndian(rec[8..], points[i].Z);
            BinaryPrimitives.WriteUInt16LittleEndian(rec[12..], points[i].Intensity);
            rec[classOffset] = points[i].Class;
        }
        return [.. h, .. body];
    }

    private static readonly Pt[] Sample = [new(0, 0, 0, 100, 2), new(1000, 2000, 300, 200, 6)];

    [Fact]
    public void Read_Format0_AppliesScaleAndUsesOrigin()
    {
        var cloud = LasReader.Read(Write("a.las", Build(2, 0, 20, Sample)));

        Assert.Equal(2, cloud.Count);
        // Pozycje względem minimum: (1000 - 0) * 0.01 = 10 itd.
        Assert.Equal([0f, 0f, 0f, 10f, 20f, 3f], cloud.Positions);
        Assert.Equal(500000.0, cloud.Origin.X, 6);
        Assert.Equal(6000000.0, cloud.Origin.Y, 6);
        Assert.Equal(100.0, cloud.Origin.Z, 6);
        Assert.Equal(new byte[] { 100, 200 }, cloud.Intensity); // <= 255: bez skalowania
        Assert.Equal(new[] { 2, 6 }, cloud.Category);
    }

    [Fact]
    public void Read_Format6_Las14_ReadsClassificationAtByte16AndScalesIntensity()
    {
        var points = new Pt[] { new(0, 0, 0, 25600, 3), new(5, 5, 5, 51200, 7) };

        var cloud = LasReader.Read(Write("a.las", Build(4, 6, 30, points)));

        Assert.Equal(new byte[] { 100, 200 }, cloud.Intensity); // 16-bit >> 8
        Assert.Equal(new[] { 3, 7 }, cloud.Category);
    }

    [Fact]
    public void Read_ExtraBytesInRecord_AreSkipped()
    {
        var cloud = LasReader.Read(Write("a.las", Build(2, 0, 26, Sample)));

        Assert.Equal([0f, 0f, 0f, 10f, 20f, 3f], cloud.Positions);
    }

    [Fact]
    public void Read_Laz_IsNotSupported() =>
        Assert.Throws<NotSupportedException>(() => LasReader.Read(Write("a.las", Build(2, 0x80 | 0, 20, Sample))));

    [Fact]
    public void Read_NotLas_Throws() =>
        Assert.Throws<InvalidDataException>(() => LasReader.Read(Write("a.las", new byte[300])));

    [Fact]
    public void Read_TruncatedPoints_Throws()
    {
        var bytes = Build(2, 0, 20, Sample);
        Assert.Throws<EndOfStreamException>(() => LasReader.Read(Write("a.las", bytes[..^5])));
    }
}
