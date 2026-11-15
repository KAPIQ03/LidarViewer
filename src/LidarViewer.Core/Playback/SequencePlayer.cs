namespace LidarViewer.Core.Playback;

/// <summary>
/// Odtwarzacz sekwencji. Nie zna UI - czas podaje metoda Advance, a o
/// zmianie kadru informuje przez zdarzenie FrameChanged.
/// </summary>
public sealed class SequencePlayer
{
    private const double MinSpeed = 0.1;
    private const double MaxSpeed = 4.0;

    private readonly IFrameSource _sequence;
    private double _position;
    private double _speed = 1.0;
    private int _startFrame;
    private int _endFrame;

    public SequencePlayer(IFrameSource sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        _sequence = sequence;
        _endFrame = sequence.FrameCount - 1;
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

    /// <summary>
    /// Pierwsza klatka zakresu odtwarzania (0-based). Odtwarzanie, petla i przewijanie nie wychodza poza zakres.
    /// </summary>
    public int StartFrame => _startFrame;

    /// <summary>
    /// Ostatnia klatka zakresu (0-based, wlacznie).
    /// </summary>
    public int EndFrame => _endFrame;

    /// <summary>
    /// Ustawia zakres odtwarzania; wartosci sa obcinane do dostepnych klatek, a poczatek nie moze byc za koncem.
    /// Biezaca pozycja zostaje wciagnieta do zakresu.
    /// </summary>
    public void SetRange(int startFrame, int endFrame)
    {
        int last = _sequence.FrameCount - 1;
        _startFrame = Math.Clamp(startFrame, 0, last);
        _endFrame = Math.Clamp(endFrame, _startFrame, last);
        Seek(_position);
    }

    private double StartTime => _sequence.Times[_startFrame];

    private double EndTime => _sequence.Times[_endFrame];

    /// <summary>
    /// Przesuwa o podana liczbe klatek (ujemna - wstecz) i zatrzymuje odtwarzanie.
    /// </summary>
    public void Step(int frames)
    {
        Pause();
        GoToFrame(FrameIndex + frames);
    }

    public void GoToFrame(int frame)
    {
        int clamped = Math.Clamp(frame, _startFrame, _endFrame);
        Seek(_sequence.Times[clamped]);
    }

    public void GoToFirst() => GoToFrame(_startFrame);

    public void GoToLast() => GoToFrame(_endFrame);

    public event Action<int>? FrameChanged;

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void Toggle() => IsPlaying = !IsPlaying;

    public void Seek(double seconds)
    {
        _position = Math.Clamp(seconds, StartTime, EndTime);
        SetFrame(_sequence.FrameAt(_position));
    }

    public void Advance(TimeSpan delta)
    {
        if (!IsPlaying)
            return;

        double target = _position + delta.TotalSeconds * Speed;

        if (target >= EndTime)
        {
            if (Loop)
            {
                target = StartTime;
            }
            else
            {
                target = EndTime;
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
