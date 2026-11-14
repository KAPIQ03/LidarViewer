using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

/// <summary>
/// Dispatcher wywolujacy wlasciwy filtr dla zadanych ustawien.
/// </summary>
public static class CloudFilters
{
    public static PointCloud Apply(PointCloud cloud, FilterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(settings);

        return settings switch
        {
            ScalarRangeSettings scalar => ScalarRangeFilter.Apply(cloud, scalar.Kind, scalar.Min, scalar.Max),
            BoxCropSettings box => BoxCropFilter.Apply(cloud, box.Min, box.Max),
            SphereCropSettings sphere => SphereCropFilter.Apply(cloud, sphere.Center, sphere.Radius),
            VoxelGridSettings voxel => VoxelGridFilter.Apply(cloud, voxel.VoxelSize),
            StatisticalOutlierSettings outlier =>
                StatisticalOutlierFilter.Apply(cloud, outlier.MeanK, outlier.StdDevThreshold),
            GroundRemovalSettings ground =>
                GroundRemovalFilter.Apply(cloud, ground.DistanceThreshold, ground.Iterations),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings, "Nieznany filtr."),
        };
    }
}
