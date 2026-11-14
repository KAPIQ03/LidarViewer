namespace LidarViewer.Core.Models;

public sealed class PointAttribute
{
    public string Name { get; }

    public float[] Values { get; }

    public float Min { get; }

    public float Max { get; }

    public PointAttribute(string name, float[] values)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(values);

        Name = name;
        Values = values;

        bool hasValue = false;
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float value in values)
        {
            if (!float.IsFinite(value)) continue;
            hasValue = true;
            if (value < min) min = value;
            if (value > max) max = value;
        }

        if (!hasValue)
        {
            Min = 0f;
            Max = 0f;
            return;
        }

        Min = min;
        Max = max;
    }
}
