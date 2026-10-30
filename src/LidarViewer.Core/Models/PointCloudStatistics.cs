namespace LidarViewer.Core.Models;

public sealed record PointCloudStatistics(
    int PointCount,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    double MinZ,
    double MaxZ,
    double AverageIntensity)
{
    public static PointCloudStatistics Calculate(IReadOnlyCollection<LidarPoint> points)
    {
        if (points.Count == 0)
        {
            return new PointCloudStatistics(0, 0, 0, 0, 0, 0, 0, 0);
        }

        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var minY = double.PositiveInfinity;
        var maxY = double.NegativeInfinity;
        var minZ = double.PositiveInfinity;
        var maxZ = double.NegativeInfinity;
        var intensitySum = 0d;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxZ = Math.Max(maxZ, point.Z);
            intensitySum += point.Intensity;
        }

        return new PointCloudStatistics(
            points.Count,
            minX,
            maxX,
            minY,
            maxY,
            minZ,
            maxZ,
            intensitySum / points.Count);
    }
}
