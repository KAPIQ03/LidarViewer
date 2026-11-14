using System.Numerics;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

public static class BoxCropFilter
{
    public static PointCloud Apply(PointCloud cloud, Vector3 min, Vector3 max)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            return CloudSubset.Empty(cloud);

        var keep = new bool[cloud.Count];
        int kept = 0;
        for (int i = 0; i < cloud.Count; i++)
        {
            float x = cloud.Positions[i * 3];
            float y = cloud.Positions[i * 3 + 1];
            float z = cloud.Positions[i * 3 + 2];

            if (x < min.X || x > max.X) continue;
            if (y < min.Y || y > max.Y) continue;
            if (z < min.Z || z > max.Z) continue;

            keep[i] = true;
            kept++;
        }

        return CloudSubset.Select(cloud, keep, kept);
    }
}
