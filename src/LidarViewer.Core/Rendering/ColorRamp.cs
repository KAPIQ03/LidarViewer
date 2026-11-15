namespace LidarViewer.Core.Rendering;

public sealed class ColorRamp
{
    private readonly Rgb[] _lut;
    private readonly double _scale;

    public ColormapKind Map { get; }

    public float Min { get; }

    public float Max { get; }

    public bool IsDegenerate => _scale == 0d;

    public ColorRamp(ColormapKind map, float min, float max, int size = Colormaps.DefaultSize)
    {
        Map = map;
        Min = min;
        Max = max;
        _lut = Colormaps.Build(map, size);

        double range = (double)max - min;
        _scale = range > 0d && double.IsFinite(range) ? 1d / range : 0d;
    }

    public Rgb ColorAt(float value)
    {
        if (_scale == 0d)
            return _lut[0];

        double t = ((double)value - Min) * _scale;
        if (double.IsNaN(t))
            return _lut[0];

        t = Math.Clamp(t, 0d, 1d);
        int index = (int)(t * (_lut.Length - 1) + 0.5d);
        return _lut[index];
    }
}
