using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

internal static class CloudSubset
{
    public static PointCloud Select(PointCloud cloud, bool[] keep, int kept)
    {
        var positions = new float[kept * 3];
        var intensity = cloud.Intensity is null ? null : new byte[kept];
        var category = cloud.Category is null ? null : new int[kept];

        int o = 0;
        for (int i = 0; i < cloud.Count; i++)
        {
            if (!keep[i]) continue;
            Array.Copy(cloud.Positions, i * 3, positions, o * 3, 3);
            if (intensity is not null) intensity[o] = cloud.Intensity![i];
            if (category is not null) category[o] = cloud.Category![i];
            o++;
        }

        return new PointCloud(positions, intensity, category) { Origin = cloud.Origin };
    }

    public static PointCloud Empty(PointCloud cloud) =>
        new([], null, null) { Origin = cloud.Origin };
}
