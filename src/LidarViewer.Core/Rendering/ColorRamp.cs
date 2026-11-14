namespace LidarViewer.Core.Rendering;

public sealed class ColorRamp
{
    private readonly Rgb[] _lut;

    public ColormapKind Map { get; }

    public float Min { get; }

    public float Max { get; }

    public ColorRamp(ColormapKind map, float min, float max, int size = Colormaps.DefaultSize)
    {
        Map = map;
        Min = min;
        Max = max;
        _lut = Colormaps.Build(map, size);
    }

    public Rgb ColorAt(float value)
    {
        if (Max <= Min)
            return _lut[0];

        float t = (value - Min) / (Max - Min);
        if (float.IsNaN(t))
            return _lut[0];

        t = Math.Clamp(t, 0f, 1f);
        int index = (int)(t * (_lut.Length - 1) + 0.5f);
        return _lut[index];
    }
}
