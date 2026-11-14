using LidarViewer.Core.Models;
using LidarViewer.Core.Spatial;

namespace LidarViewer.Core.Filters;

public static class StatisticalOutlierFilter
{
    public static PointCloud Apply(PointCloud cloud, int meanK, float stdDevThreshold)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentOutOfRangeException.ThrowIfLessThan(meanK, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(stdDevThreshold);

        int count = cloud.Count;
        int neighbors = Math.Min(meanK, count - 1);
        if (neighbors <= 0)
            return KeepAll(cloud);

        var tree = new KdTree(cloud.Positions, count);
        var meanDistances = new double[count];

        // Drzewo jest tylko do odczytu podczas wyszukiwania, a wynik dla
        // punktu zalezy wylacznie od jego sasiadow - obliczenia dla roznych
        // punktow sa niezalezne i daja sie wykonac rownolegle.
        Parallel.For(
            0,
            count,
            () => new double[neighbors],
            (i, _, buffer) =>
            {
                meanDistances[i] = tree.SumOfNearestDistances(i, neighbors, buffer) / neighbors;
                return buffer;
            },
            _ => { });

        double sum = 0;
        for (int i = 0; i < count; i++) sum += meanDistances[i];

        // Punkt jest odosobniony, gdy jego otoczenie jest rzadsze niz przecietna
        // chmura: srednia odleglosc do sasiadow przekracza prog razy srednia
        // odleglosc w calej chmurze. Kryterium odnosi sie do gestosci, wiec
        // nie zalezy od jednostek - chmura w metrach i w milimetrach daje ten
        // sam wynik.
        double average = sum / count;
        if (average <= 0)
            return KeepAll(cloud);

        double limit = stdDevThreshold * average;
        var keep = new bool[count];
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            if (meanDistances[i] > limit) continue;
            keep[i] = true;
            kept++;
        }

        return CloudSubset.Select(cloud, keep, kept);
    }

    private static PointCloud KeepAll(PointCloud cloud)
    {
        var keep = new bool[cloud.Count];
        for (int i = 0; i < keep.Length; i++) keep[i] = true;
        return CloudSubset.Select(cloud, keep, keep.Length);
    }
}
