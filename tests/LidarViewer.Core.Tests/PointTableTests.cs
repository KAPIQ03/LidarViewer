using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class PointTableTests
{
    [Fact]
    public void ColumnsDependOnAvailableAttributes()
    {
        var plain = new PointTable(new PointCloud([1f, 2f, 3f]));
        var full = new PointTable(new PointCloud([1f, 2f, 3f], [7], [3])
        {
            LaserId = [4],
            Azimuth = [90f],
            Time = [0.5f],
        });

        Assert.Equal(["#", "X", "Y", "Z", "distance"], plain.ColumnNames);
        Assert.Equal(["#", "X", "Y", "Z", "intensity", "laser_id", "azimuth", "time_s", "category", "distance"], full.ColumnNames);
    }

    [Fact]
    public void CellsUseInvariantFormatAndWorldCoordinates()
    {
        var cloud = new PointCloud([1.5f, 2f, 3f], [200]) { Origin = (10, 0, 0), LaserId = [5] };
        var table = new PointTable(cloud);

        string[] cells = table.Cells(0);

        Assert.Equal("1", cells[0]);
        Assert.Equal("11.500", cells[1]);
        Assert.Equal("200", cells[4]);
        Assert.Equal("5", cells[5]);
        Assert.Equal(Math.Sqrt(11.5 * 11.5 + 4 + 9).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), cells[^1]);
    }

    [Fact]
    public void RowsAlignWithHeader()
    {
        var table = new PointTable(new PointCloud([1f, 2f, 3f, 4f, 5f, 6f], [1, 2]));

        Assert.Equal(table.Header.Length, table[0].Length);
        Assert.Equal(table.Header.Length, table[1].Length);
    }

    [Fact]
    public void ListIsReadOnlyAndIndexable()
    {
        var table = new PointTable(new PointCloud([1f, 2f, 3f, 4f, 5f, 6f]));
        System.Collections.IList list = table;

        Assert.Equal(2, table.Count);
        Assert.Equal(table[1], list[1]);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add("x"));
        Assert.Equal(2, table.Count());
    }

    [Fact]
    public void OutOfRangeRowThrows()
    {
        var table = new PointTable(new PointCloud([1f, 2f, 3f]));

        Assert.Throws<ArgumentOutOfRangeException>(() => table[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Cells(-1));
    }

    [Fact]
    public void EmptyCloudGivesEmptyTable()
    {
        var table = new PointTable(new PointCloud([]));

        Assert.Equal(0, table.Count);
        Assert.NotEmpty(table.Header);
    }
}
