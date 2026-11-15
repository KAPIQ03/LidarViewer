using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

internal static class CloudSubset
{
    public static PointCloud Select(PointCloud cloud, bool[] keep, int kept) => cloud.Take(keep, kept);

    public static PointCloud Empty(PointCloud cloud) =>
        new([], null, null) { Origin = cloud.Origin };
}
