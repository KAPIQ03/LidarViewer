namespace LidarViewer.Core.Playback;

internal static class FrameTimes
{
    /// <summary>
    /// Indeks ostatniej klatki, ktorej czas nie jest pozniejszy niz pozycja (binarnie).
    /// Przed pierwsza klatka zwraca 0.
    /// </summary>
    public static int IndexAt(IReadOnlyList<double> times, double positionSeconds)
    {
        int low = 0;
        int high = times.Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (times[mid] <= positionSeconds)
                low = mid;
            else
                high = mid - 1;
        }

        return low;
    }
}
