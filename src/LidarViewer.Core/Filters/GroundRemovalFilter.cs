using System.Numerics;
using LidarViewer.Core.Models;
using LidarViewer.Core.Spatial;

namespace LidarViewer.Core.Filters;

/// <summary>
/// Usuwanie podlogi metoda RANSAC: dopasowuje plaszczyzne ziemi na
/// podstawie losowych probek, a nastepnie kasuje punkty lezace blisko
/// dopasowanej plaszczyzny.
/// </summary>
public static class GroundRemovalFilter
{
    private const int MaximumSample = 5000;
    private const int Seed = 12345;

    public static PointCloud Apply(PointCloud cloud, float distanceThreshold, int iterations)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentOutOfRangeException.ThrowIfNegative(distanceThreshold);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        int count = cloud.Count;
        if (count < 3) return KeepAll(cloud);

        float[] positions = cloud.Positions;
        int[] sample = SelectSample(count);
        var random = new Random(Seed);

        PlaneModel best = default;
        int bestInliers = -1;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int a = sample[random.Next(sample.Length)];
            int b = sample[random.Next(sample.Length)];
            int c = sample[random.Next(sample.Length)];
            if (a == b || a == c || b == c) continue;

            if (!PlaneModel.TryFromThreePoints(Point(positions, a), Point(positions, b), Point(positions, c), out PlaneModel plane))
                continue;

            int inliers = CountInliers(positions, sample, plane, distanceThreshold);
            if (inliers > bestInliers)
            {
                bestInliers = inliers;
                best = plane;
            }
        }

        if (bestInliers <= 0) return KeepAll(cloud);

        // Dopasowanie refine: najlepsza plaszczyzna z RANSAC przechodzi
        // dokladnie przez trzy punkty, co przy szumie daje lekki blad.
        // Ponowne dopasowanie na wszystkich inlierach daje model blizszy
        // rzeczywistemu terenowi. Refinement jest odrzucany, gdy pogarsza
        // wynik - np. dla chmury zdegenerowanej do linii.
        int[] inlierIndices = CollectInliers(positions, count, best, distanceThreshold);
        if (inlierIndices.Length >= 3)
        {
            var inlierPoints = new Vector3[inlierIndices.Length];
            for (int i = 0; i < inlierIndices.Length; i++)
                inlierPoints[i] = Point(positions, inlierIndices[i]);

            PlaneModel refined = PlaneModel.FitLeastSquares(inlierPoints);
            if (CollectInliers(positions, count, refined, distanceThreshold).Length >= inlierIndices.Length)
                best = refined;
        }

        var keep = new bool[count];
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            float distance = best.DistanceTo(Point(positions, i));
            if (distance <= distanceThreshold) continue;
            keep[i] = true;
            kept++;
        }

        return CloudSubset.Select(cloud, keep, kept);
    }

    /// <summary>
    /// Probka do losowania: wszystkie punkty albo ich rowno rozlozona podprobe.
    /// </summary>
    private static int[] SelectSample(int count)
    {
        if (count <= MaximumSample)
        {
            var all = new int[count];
            for (int i = 0; i < count; i++) all[i] = i;
            return all;
        }

        int step = count / MaximumSample;
        var sample = new int[MaximumSample];
        for (int i = 0; i < MaximumSample; i++) sample[i] = i * step;
        return sample;
    }

    private static int CountInliers(float[] positions, int[] indices, PlaneModel plane, float threshold)
    {
        int inliers = 0;
        for (int i = 0; i < indices.Length; i++)
            if (plane.DistanceTo(Point(positions, indices[i])) <= threshold) inliers++;

        return inliers;
    }

    private static int[] CollectInliers(float[] positions, int count, PlaneModel plane, float threshold)
    {
        var inliers = new List<int>();
        for (int i = 0; i < count; i++)
            if (plane.DistanceTo(Point(positions, i)) <= threshold) inliers.Add(i);

        return [.. inliers];
    }

    private static Vector3 Point(float[] positions, int index) =>
        new(positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2]);

    private static PointCloud KeepAll(PointCloud cloud)
    {
        var keep = new bool[cloud.Count];
        for (int i = 0; i < keep.Length; i++) keep[i] = true;
        return CloudSubset.Select(cloud, keep, keep.Length);
    }
}
