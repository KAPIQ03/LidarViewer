using System.Numerics;

namespace LidarViewer.Core.Models;

public sealed class PointCloud
{
    public float[] Positions { get; }

    public byte[]? Intensity { get; }

    public int[]? Category { get; }

    /// <summary>
    /// Numer lasera (kanalu) czujnika dla kazdego punktu. null, gdy zrodlo go nie zna.
    /// </summary>
    public byte[]? LaserId { get; init; }

    /// <summary>
    /// Azymut czujnika w stopniach (0-360) w chwili pomiaru punktu.
    /// </summary>
    public float[]? Azimuth { get; init; }

    /// <summary>
    /// Czas pomiaru w sekundach wzgledem poczatku chmury (klatki).
    /// </summary>
    public float[]? Time { get; init; }

    public (double X, double Y, double Z) Origin { get; init; }

    public int Count => Positions.Length / 3;

    public Vector3 BoundsMin { get; }

    public Vector3 BoundsMax { get; }

    public PointCloud(float[] positions, byte[]? intensity = null, int[]? category = null)
    {
        if (positions.Length % 3 != 0)
            throw new ArgumentException("Długość tablicy pozycji musi być wielokrotnością 3.", nameof(positions));

        Positions = positions;
        Intensity = intensity;
        Category = category;
        (BoundsMin, BoundsMax) = ComputeBounds(positions);
    }

    public Vector3 Center => (BoundsMin + BoundsMax) / 2f;

    public float Radius => Vector3.Distance(BoundsMin, BoundsMax) / 2f;

    private static (Vector3 Min, Vector3 Max) ComputeBounds(float[] p)
    {
        if (p.Length == 0)
            return (Vector3.Zero, Vector3.Zero);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < p.Length; i += 3)
        {
            var v = new Vector3(p[i], p[i + 1], p[i + 2]);
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return (min, max);
    }

    private readonly Dictionary<ScalarKind, PointAttribute> _attributeCache = [];

    public PointAttribute? GetAttribute(ScalarKind kind)
    {
        if (_attributeCache.TryGetValue(kind, out var cached))
            return cached;

        PointAttribute? computed = kind switch
        {
            ScalarKind.Intensity => BuildIntensityAttribute(),
            ScalarKind.Distance => BuildDistanceAttribute(),
            ScalarKind.Height => BuildHeightAttribute(),
            ScalarKind.LaserId => LaserId is null ? null : ToAttribute("laser_id", LaserId),
            ScalarKind.Azimuth => Azimuth is null ? null : new PointAttribute("azimuth", Azimuth),
            ScalarKind.Time => Time is null ? null : new PointAttribute("time", Time),
            _ => null,
        };

        if (computed is not null)
            _attributeCache[kind] = computed;

        return computed;
    }

    private static PointAttribute ToAttribute(string name, byte[] source)
    {
        var values = new float[source.Length];
        for (int i = 0; i < source.Length; i++)
            values[i] = source[i];

        return new PointAttribute(name, values);
    }

    private PointAttribute? BuildIntensityAttribute()
    {
        if (Intensity is null) return null;

        var values = new float[Intensity.Length];
        for (int i = 0; i < Intensity.Length; i++)
            values[i] = Intensity[i];

        return new PointAttribute("intensity", values);
    }

    private PointAttribute BuildDistanceAttribute()
    {
        var values = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            double x = Positions[i * 3] + Origin.X;
            double y = Positions[i * 3 + 1] + Origin.Y;
            double z = Positions[i * 3 + 2] + Origin.Z;
            values[i] = (float)Math.Sqrt(x * x + y * y + z * z);
        }

        return new PointAttribute("distance", values);
    }

    private PointAttribute BuildHeightAttribute()
    {
        var values = new float[Count];
        for (int i = 0; i < Count; i++)
            values[i] = Positions[i * 3 + 2] + (float)Origin.Z;

        return new PointAttribute("height", values);
    }

    public PointCloud WithoutInvalidPoints()
    {
        var keep = new bool[Count];
        int valid = 0;
        for (int i = 0; i < Count; i++)
        {
            keep[i] = IsValid(i);
            if (keep[i]) valid++;
        }

        return valid == Count ? this : Take(keep, valid);
    }

    /// <summary>
    /// Nowa chmura z punktow oznaczonych w keep (kept = ich liczba). Przenosi wszystkie atrybuty
    /// punktow oraz Origin - jedyne miejsce, ktore trzeba rozszerzyc przy nowym atrybucie.
    /// </summary>
    public PointCloud Take(bool[] keep, int kept)
    {
        ArgumentNullException.ThrowIfNull(keep);

        var positions = new float[kept * 3];
        var intensity = Intensity is null ? null : new byte[kept];
        var category = Category is null ? null : new int[kept];
        var laserId = LaserId is null ? null : new byte[kept];
        var azimuth = Azimuth is null ? null : new float[kept];
        var time = Time is null ? null : new float[kept];

        int o = 0;
        for (int i = 0; i < Count; i++)
        {
            if (!keep[i]) continue;
            Array.Copy(Positions, i * 3, positions, o * 3, 3);
            if (intensity != null) intensity[o] = Intensity![i];
            if (category != null) category[o] = Category![i];
            if (laserId != null) laserId[o] = LaserId![i];
            if (azimuth != null) azimuth[o] = Azimuth![i];
            if (time != null) time[o] = Time![i];
            o++;
        }

        return new PointCloud(positions, intensity, category)
        {
            Origin = Origin,
            LaserId = laserId,
            Azimuth = azimuth,
            Time = time,
        };
    }

    private bool IsValid(int i) =>
        float.IsFinite(Positions[i * 3]) && float.IsFinite(Positions[i * 3 + 1]) && float.IsFinite(Positions[i * 3 + 2]);
}
