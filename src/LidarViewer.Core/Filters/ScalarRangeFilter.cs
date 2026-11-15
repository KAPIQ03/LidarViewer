using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

public static class ScalarRangeFilter
{
    public static PointCloud Apply(PointCloud cloud, ScalarKind kind, float min, float max)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        var attribute = cloud.GetAttribute(kind);
        if (attribute is null || min > max)
            return CloudSubset.Empty(cloud);

        int count = cloud.Count;
        int values = Math.Min(count, attribute.Values.Length);

        var keep = new bool[count];
        int kept = 0;
        for (int i = 0; i < values; i++)
        {
            float value = attribute.Values[i];
            if (value < min || value > max) continue;
            keep[i] = true;
            kept++;
        }

        return CloudSubset.Select(cloud, keep, kept);
    }
}
