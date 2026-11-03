using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Tests;

public class CsvReaderTests : TempDirTestBase
{
    [Fact]
    public void Read_HeaderWithCommas_FindsColumnsByName()
    {
        var cloud = CsvReader.Read(Write("a.csv", "id,Z,X,Y,Intensity\n1,3,1,2,50\n2,6,4,5,60\n"));

        Assert.Equal(2, cloud.Count);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 50, 60 }, cloud.Intensity);
    }

    [Fact]
    public void Read_NoHeaderWhitespace_UsesFirstThreeColumnsAndOptionalIntensity()
    {
        var cloud = CsvReader.Read(Write("a.csv", "# komentarz\n1 2 3 10\n4.5  5  6 20\n"));

        Assert.Equal([1f, 2f, 3f, 4.5f, 5f, 6f], cloud.Positions);
        Assert.Equal(new byte[] { 10, 20 }, cloud.Intensity);
    }

    [Fact]
    public void Read_SemicolonWithDecimalComma()
    {
        var cloud = CsvReader.Read(Write("a.csv", "x;y;z\n1,5;2,5;3,5\n"));

        Assert.Equal([1.5f, 2.5f, 3.5f], cloud.Positions);
        Assert.Null(cloud.Intensity);
    }

    [Fact]
    public void Read_HeaderWithoutZ_Throws() =>
        Assert.Throws<InvalidDataException>(() => CsvReader.Read(Write("a.csv", "x,y\n1,2\n")));

    [Fact]
    public void Read_NotANumber_ThrowsWithLineNumber()
    {
        var ex = Assert.Throws<InvalidDataException>(() => CsvReader.Read(Write("a.csv", "1,2,3\n4,abc,6\n")));
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void Read_TooFewColumns_Throws() =>
        Assert.Throws<InvalidDataException>(() => CsvReader.Read(Write("a.csv", "1,2,3\n4,5\n")));

    [Fact]
    public void Read_EmptyFile_ReturnsEmptyCloud() =>
        Assert.Equal(0, CsvReader.Read(Write("a.csv", "")).Count);
}
