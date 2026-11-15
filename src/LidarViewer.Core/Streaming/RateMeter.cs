namespace LidarViewer.Core.Streaming;

/// <summary>
/// Mierzy tempo zdarzen (np. pakietow na sekunde) w przesuwanym oknie czasu.
/// Bezpieczny watkowo - zapis z watku odbioru, odczyt z watku interfejsu.
/// </summary>
public sealed class RateMeter
{
    private readonly TimeSpan _window;
    private readonly Queue<(DateTimeOffset Time, long Count)> _events = new();
    private readonly object _gate = new();
    private long _inWindow;

    public RateMeter(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(1);
        if (_window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));
    }

    public void Add(DateTimeOffset now, long count = 1)
    {
        lock (_gate)
        {
            _events.Enqueue((now, count));
            _inWindow += count;
            Trim(now);
        }
    }

    public void Add(long count = 1) => Add(DateTimeOffset.UtcNow, count);

    /// <summary>Zdarzenia na sekunde w oknie konczacym sie w chwili <paramref name="now"/>.</summary>
    public double PerSecond(DateTimeOffset now)
    {
        lock (_gate)
        {
            Trim(now);
            return _inWindow / _window.TotalSeconds;
        }
    }

    public double PerSecond() => PerSecond(DateTimeOffset.UtcNow);

    private void Trim(DateTimeOffset now)
    {
        while (_events.Count > 0 && now - _events.Peek().Time >= _window)
            _inWindow -= _events.Dequeue().Count;
    }
}
