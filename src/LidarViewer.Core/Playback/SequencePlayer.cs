namespace LidarViewer.Core.Playback;

/// <summary>
/// Odtwarzacz sekwencji. Nie zna UI - czas podaje metoda Advance, a o
/// zmianie kadru informuje przez zdarzenie FrameChanged.
/// </summary>
public sealed class SequencePlayer
{
    private const double MinSpeed = 0.25;
    private const double MaxSpeed = 4.0;

    private readonly IFrameSource _sequence;
    private double _position;
    private double _speed = 1.0;

    public SequencePlayer(IFrameSource sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        _sequence = sequence;
    }

    public IFrameSource Sequence => _sequence;

    public int FrameIndex { get; private set; }

    public double PositionSeconds => _position;

    public bool IsPlaying { get; private set; }

    public double Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, MinSpeed, MaxSpeed);
    }

    public bool Loop { get; set; }

    public event Action<int>? FrameChanged;

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void Toggle() => IsPlaying = !IsPlaying;

    public void Seek(double seconds)
    {
        _position = Math.Clamp(seconds, 0, _sequence.Duration);
        SetFrame(_sequence.FrameAt(_position));
    }

    public void Advance(TimeSpan delta)
    {
        if (!IsPlaying)
            return;

        double target = _position + delta.TotalSeconds * Speed;

        if (target >= _sequence.Duration)
        {
            if (Loop)
            {
                target = 0;
            }
            else
            {
                target = _sequence.Duration;
                IsPlaying = false;
            }
        }

        _position = target;
        SetFrame(_sequence.FrameAt(_position));
    }

    private void SetFrame(int index)
    {
        if (index == FrameIndex)
            return;

        FrameIndex = index;
        FrameChanged?.Invoke(index);
    }
}
