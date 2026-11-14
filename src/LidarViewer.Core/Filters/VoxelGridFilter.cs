using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

public static class VoxelGridFilter
{
    private const int AxisOffset = 1 << 20;

    public static PointCloud Apply(PointCloud cloud, float voxelSize)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(voxelSize);

        int count = cloud.Count;
        var slots = new Dictionary<long, int>(count);
        var order = new List<long>();
        var sums = new List<double>(count * 3);
        var counts = new List<int>(count);
        var intensities = new List<byte>(count);
        var categories = new List<int>(count);

        bool hasIntensity = cloud.Intensity is not null;
        bool hasCategory = cloud.Category is not null;

        for (int i = 0; i < count; i++)
        {
            float x = cloud.Positions[i * 3];
            float y = cloud.Positions[i * 3 + 1];
            float z = cloud.Positions[i * 3 + 2];

            long key = Key(x, y, z, voxelSize);
            if (!slots.TryGetValue(key, out int slot))
            {
                slot = order.Count;
                slots[key] = slot;
                order.Add(key);
                sums.Add(0);
                sums.Add(0);
                sums.Add(0);
                counts.Add(0);
                intensities.Add(0);
                categories.Add(0);
            }

            if (counts[slot] == 0)
            {
                if (hasIntensity) intensities[slot] = cloud.Intensity![i];
                if (hasCategory) categories[slot] = cloud.Category![i];
            }

            sums[slot * 3] += x;
            sums[slot * 3 + 1] += y;
            sums[slot * 3 + 2] += z;
            counts[slot]++;
        }

        var positions = new float[order.Count * 3];
        var intensity = hasIntensity ? new byte[order.Count] : null;
        var category = hasCategory ? new int[order.Count] : null;

        for (int slot = 0; slot < order.Count; slot++)
        {
            float divisor = counts[slot];
            positions[slot * 3] = (float)(sums[slot * 3] / divisor);
            positions[slot * 3 + 1] = (float)(sums[slot * 3 + 1] / divisor);
            positions[slot * 3 + 2] = (float)(sums[slot * 3 + 2] / divisor);

            if (intensity is not null) intensity[slot] = intensities[slot];
            if (category is not null) category[slot] = categories[slot];
        }

        return new PointCloud(positions, intensity, category) { Origin = cloud.Origin };
    }

    private static long Key(float x, float y, float z, float voxelSize)
    {
        long ix = (long)Math.Floor(x / voxelSize) + AxisOffset;
        long iy = (long)Math.Floor(y / voxelSize) + AxisOffset;
        long iz = (long)Math.Floor(z / voxelSize) + AxisOffset;

        return (ix << 42) | (iy << 21) | iz;
    }
}
