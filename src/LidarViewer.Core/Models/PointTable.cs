using System.Collections;
using System.Globalization;

namespace LidarViewer.Core.Models;

/// <summary>
/// Widok chmury jako tabela (jak "Spreadsheet View" w VeloView): wiersz na punkt, kolumny X, Y, Z oraz
/// atrybuty, ktore chmura posiada. Wiersze sa formatowane dopiero na zadanie, wiec lista z milionami
/// punktow nic nie kosztuje przed przewinieciem - nadaje sie jako zrodlo zwirtualizowanej listy.
/// </summary>
public sealed class PointTable : IReadOnlyList<string>, IList
{
    private readonly PointCloud _cloud;
    private readonly Column[] _columns;

    public PointTable(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        _cloud = cloud;

        var columns = new List<Column>
        {
            new("#", 9, i => (i + 1).ToString(CultureInfo.InvariantCulture)),
            new("X", 11, i => Number(cloud.Positions[i * 3] + cloud.Origin.X)),
            new("Y", 11, i => Number(cloud.Positions[i * 3 + 1] + cloud.Origin.Y)),
            new("Z", 11, i => Number(cloud.Positions[i * 3 + 2] + cloud.Origin.Z)),
        };

        if (cloud.Intensity is { } intensity)
            columns.Add(new("intensity", 9, i => intensity[i].ToString(CultureInfo.InvariantCulture)));
        if (cloud.LaserId is { } laser)
            columns.Add(new("laser_id", 8, i => laser[i].ToString(CultureInfo.InvariantCulture)));
        if (cloud.Azimuth is { } azimuth)
            columns.Add(new("azimuth", 9, i => Number(azimuth[i])));
        if (cloud.Time is { } time)
            columns.Add(new("time_s", 10, i => time[i].ToString("0.000000", CultureInfo.InvariantCulture)));
        if (cloud.Category is { } category)
            columns.Add(new("category", 9, i => category[i].ToString(CultureInfo.InvariantCulture)));

        columns.Add(new("distance", 10, i => Number(Distance(i))));
        _columns = columns.ToArray();
    }

    public IReadOnlyList<string> ColumnNames => _columns.Select(c => c.Name).ToArray();

    /// <summary>
    /// Naglowek wyrownany do tych samych szerokosci co wiersze (czcionka o stalej szerokosci).
    /// </summary>
    public string Header => string.Concat(_columns.Select(c => c.Name.PadLeft(c.Width)));

    public int Count => _cloud.Count;

    /// <summary>
    /// Wartosci komorek wiersza jako tekst, w kolejnosci ColumnNames.
    /// </summary>
    public string[] Cells(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return _columns.Select(c => c.Format(index)).ToArray();
    }

    public string this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return string.Concat(_columns.Select(c => c.Format(index).PadLeft(c.Width)));
        }
    }

    private double Distance(int i)
    {
        double x = _cloud.Positions[i * 3] + _cloud.Origin.X;
        double y = _cloud.Positions[i * 3 + 1] + _cloud.Origin.Y;
        double z = _cloud.Positions[i * 3 + 2] + _cloud.Origin.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    private static string Number(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    public IEnumerator<string> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed record Column(string Name, int Width, Func<int, string> Format);

    // --- IList (tylko do odczytu): wymagane, by lista mogla byc zrodlem wirtualizowanego ItemsControl ---

    bool IList.IsReadOnly => true;

    bool IList.IsFixedSize => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    int IList.Add(object? value) => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();

    bool IList.Contains(object? value) => throw new NotSupportedException();

    int IList.IndexOf(object? value) => throw new NotSupportedException();

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();

    void ICollection.CopyTo(Array array, int index)
    {
        for (int i = 0; i < Count; i++)
            array.SetValue(this[i], index + i);
    }
}
