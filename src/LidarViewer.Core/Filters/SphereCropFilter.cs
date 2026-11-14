using System.Numerics;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

public static class SphereCropFilter
{
    public static PointCloud Apply(PointCloud cloud, Vector3 center, float radius)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        double limit = (double)radius * radius;
        var keep = new bool[cloud.Count];
        int kept = 0;
        for (int i = 0; i < cloud.Count; i++)
        {
            double dx = cloud.Positions[i * 3] - center.X;
            double dy = cloud.Positions[i * 3 + 1] - center.Y;
            double dz = cloud.Positions[i * 3 + 2] - center.Z;

            if (dx * dx + dy * dy + dz * dz > limit) continue;
            keep[i] = true;
            kept++;
        }

        return CloudSubset.Select(cloud, keep, kept);
    }
}
