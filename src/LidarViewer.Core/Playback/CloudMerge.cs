using LidarViewer.Core.Models;

namespace LidarViewer.Core.Playback;

public static class CloudMerge
{
    /// <summary>
    /// Skleja chmury w jedna (np. klatka z kilkoma poprzednimi - "trailing frames"). Atrybut obecny
    /// tylko w czesci chmur dostaje zera w pozostalych. Origin pochodzi z ostatniej chmury.
    /// </summary>
    public static PointCloud Concatenate(IReadOnlyList<PointCloud> clouds)
    {
        ArgumentNullException.ThrowIfNull(clouds);
        if (clouds.Count == 0)
            return new PointCloud([]);
        if (clouds.Count == 1)
            return clouds[0];

        int total = clouds.Sum(c => c.Count);
        var positions = new float[total * 3];
        byte[]? intensity = clouds.Any(c => c.Intensity is not null) ? new byte[total] : null;
        int[]? category = clouds.Any(c => c.Category is not null) ? new int[total] : null;
        byte[]? laserId = clouds.Any(c => c.LaserId is not null) ? new byte[total] : null;
        float[]? azimuth = clouds.Any(c => c.Azimuth is not null) ? new float[total] : null;
        float[]? time = clouds.Any(c => c.Time is not null) ? new float[total] : null;

        int at = 0;
        foreach (PointCloud cloud in clouds)
        {
            Array.Copy(cloud.Positions, 0, positions, at * 3, cloud.Positions.Length);
            Copy(cloud.Intensity, intensity, at, cloud.Count);
            Copy(cloud.Category, category, at, cloud.Count);
            Copy(cloud.LaserId, laserId, at, cloud.Count);
            Copy(cloud.Azimuth, azimuth, at, cloud.Count);
            Copy(cloud.Time, time, at, cloud.Count);
            at += cloud.Count;
        }

        return new PointCloud(positions, intensity, category)
        {
            Origin = clouds[^1].Origin,
            LaserId = laserId,
            Azimuth = azimuth,
            Time = time,
        };
    }

    private static void Copy<T>(T[]? source, T[]? target, int at, int count)
    {
        if (source is null || target is null)
            return;

        Array.Copy(source, 0, target, at, Math.Min(count, source.Length));
    }
}
