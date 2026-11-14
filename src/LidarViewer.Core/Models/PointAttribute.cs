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

        if (values.Length == 0)
        {
            Min = 0f;
            Max = 0f;
            return;
        }

        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float value in values)
        {
            if (float.IsNaN(value)) continue;
            if (value < min) min = value;
            if (value > max) max = value;
        }

        Min = min == float.MaxValue ? 0f : min;
        Max = max == float.MinValue ? 0f : max;
    }
}
