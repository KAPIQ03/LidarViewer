namespace LidarViewer.Core.Readers;

/// <summary>
/// Zbiera punkty dodawane po jednym (z czytników tekstowych i binarnych wierszy)
/// i na końcu buduje PointCloud. Atrybuty opcjonalne są zbierane tylko, gdy plik je ma.
/// </summary>
internal sealed class PointAccumulator
{
    private readonly List<float> _positions;
    private readonly List<byte>? _intensity;
    private readonly List<int>? _category;

    public PointAccumulator(bool hasIntensity, bool hasCategory, long expectedCount = 0)
    {
        int capacity = (int)Math.Clamp(expectedCount, 0, 1 << 24); // ograniczenie przy błędnym nagłówku
        _positions = new List<float>(capacity * 3);
        _intensity = hasIntensity ? new List<byte>(capacity) : null;
        _category = hasCategory ? new List<int>(capacity) : null;
    }

    public void Add(double x, double y, double z, double intensity = 0, int category = 0)
    {
        _positions.Add((float)x);
        _positions.Add((float)y);
        _positions.Add((float)z);
        _intensity?.Add(ScalarTypes.ToByte(intensity));
        _category?.Add(category);
    }

    public Models.PointCloud ToCloud() =>
        new(_positions.ToArray(), _intensity?.ToArray(), _category?.ToArray());
}
