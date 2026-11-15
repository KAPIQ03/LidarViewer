using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.App.Streaming;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Readers;
using LidarViewer.Core.Recording;
using LidarViewer.Core.Rendering;
using LidarViewer.Core.Streaming;

namespace LidarViewer.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequence))]
    [NotifyPropertyChangedFor(nameof(AutoFitCamera))]
    [NotifyPropertyChangedFor(nameof(AutoResetView))]
    [NotifyPropertyChangedFor(nameof(FrameInfo))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public partial IFrameSource? Sequence { get; set; }

    partial void OnSequenceChanged(IFrameSource? value) => OnPropertyChanged(nameof(StatusInfo));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequence))]
    [NotifyPropertyChangedFor(nameof(FrameInfo))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public partial SequencePlayer? Player { get; set; }

    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    public bool HasSequence => Sequence is not null;

    public bool IsPlaying => Player?.IsPlaying ?? false;

    public double Duration => Sequence?.Duration ?? 0;

    public string FrameInfo => Sequence is null
        ? string.Empty
        : $"Klatka {Player?.FrameIndex + 1} / {Sequence.FrameCount}";

    public bool IsScrubbing { get; set; }

    public IReadOnlyList<PlaybackSpeed> Speeds { get; } =
    [
        new PlaybackSpeed(0.1, "0.1x"),
        new PlaybackSpeed(0.25, "0.25x"),
        new PlaybackSpeed(0.5, "0.5x"),
        new PlaybackSpeed(1.0, "1x"),
        new PlaybackSpeed(2.0, "2x"),
        new PlaybackSpeed(4.0, "4x"),
    ];

    [ObservableProperty]
    public partial PlaybackSpeed? Speed { get; set; }

    public IRelayCommand PlayPauseCommand { get; }

    public const int MaxTrailingFrames = 10;

    [ObservableProperty]
    public partial int TrailingFrames { get; set; }

    partial void OnTrailingFramesChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, MaxTrailingFrames);
        if (clamped != value)
        {
            TrailingFrames = clamped;
            return;
        }

        if (Player is not null)
            ShowFrame(Player.FrameIndex);
    }

    public int FrameNumber
    {
        get => Player is null ? 0 : Player.FrameIndex + 1;
        set
        {
            if (Player is null)
                return;

            Player.GoToFrame(value - 1);
            SyncPlaybackState();
        }
    }

    public int TotalFrames => Sequence?.FrameCount ?? 0;

    public int RangeStart
    {
        get => Player is null ? 0 : Player.StartFrame + 1;
        set
        {
            if (Player is null)
                return;

            Player.SetRange(value - 1, Player.EndFrame);
            SyncPlaybackState();
        }
    }

    public int RangeEnd
    {
        get => Player is null ? 0 : Player.EndFrame + 1;
        set
        {
            if (Player is null)
                return;

            Player.SetRange(Player.StartFrame, value - 1);
            SyncPlaybackState();
        }
    }

    [RelayCommand]
    private void StepForward() => StepFrames(1);

    [RelayCommand]
    private void StepBack() => StepFrames(-1);

    [RelayCommand]
    private void FirstFrame()
    {
        Player?.GoToFirst();
        SyncPlaybackState();
    }

    [RelayCommand]
    private void LastFrame()
    {
        Player?.GoToLast();
        SyncPlaybackState();
    }

    public void StepFrames(int frames)
    {
        Player?.Step(frames);
        SyncPlaybackState();
    }

    private void SyncPlaybackState()
    {
        if (Player is null)
            return;

        PositionSeconds = Player.PositionSeconds;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(FrameNumber));
        OnPropertyChanged(nameof(FrameInfo));
        OnPropertyChanged(nameof(RangeStart));
        OnPropertyChanged(nameof(RangeEnd));
    }

    public IRelayCommand<string> LoadSequenceCommand { get; }

    private static bool IsPcapFile(string path)
    {
        string extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".pcap", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pcapng", StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadPcapAsync(string path)
    {
        VelodyneCalibration calibration;
        try
        {
            calibration = LoadCalibration();
        }
        catch (Exception ex)
        {
            Status = $"Błąd kalibracji: {ex.Message}";
            return;
        }

        Status = $"Indeksowanie pcap (Velodyne {calibration.Name})...";
        PcapFrameSource source;
        try
        {
            source = await Task.Run(() => new PcapFrameSource(path, calibration));
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
            return;
        }

        StartPlayback(source);
    }

    private void CloseSequence()
    {
        if (Player is { } player)
        {
            player.Pause();
            player.FrameChanged -= OnFrameChanged;
        }

        Player = null;
        Sequence = null;
        PositionSeconds = 0;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(FrameNumber));
        OnPropertyChanged(nameof(RangeStart));
        OnPropertyChanged(nameof(RangeEnd));
    }

    private void ApplySource(PointCloud cloud)
    {
        FitVersion++;
        SourceCloud = cloud;
        Pipeline.Reset();
        Pipeline.Source = cloud;
        PointCloud = Pipeline.Output;
    }

    public bool Loop
    {
        get => Player?.Loop ?? false;
        set
        {
            if (Player is not null)
                Player.Loop = value;
            OnPropertyChanged(nameof(Loop));
        }
    }

    partial void OnSpeedChanged(PlaybackSpeed? value)
    {
        if (Player is not null && value is not null)
            Player.Speed = value.Multiplier;
    }

    private void PlayPauseCore()
    {
        if (Player is null)
            return;

        Player.Toggle();
        OnPropertyChanged(nameof(IsPlaying));
    }

    private void LoadSequenceCore(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            StartPlayback(SequenceLoader.Load(directory));
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    private void StartPlayback(IFrameSource source)
    {
        try
        {
            var player = new SequencePlayer(source) { Speed = Speed?.Multiplier ?? 1.0 };
            player.FrameChanged += OnFrameChanged;

            if (Player is { } old)
            {
                old.Pause();
                old.FrameChanged -= OnFrameChanged;
            }

            FitVersion++;
            Sequence = source;
            Player = player;
            Pipeline.Reset();
            PositionSeconds = 0;
            ShowFrame(0);
            Status = $"Wczytano sekwencję „{source.Name}” ({source.FrameCount} klatek).";
            OnPropertyChanged(nameof(TotalFrames));
            SyncPlaybackState();
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    private void OnFrameChanged(int index) => ShowFrame(index);

    private void ShowFrame(int index)
    {
        if (Sequence is null)
            return;

        int first = Math.Max(0, index - TrailingFrames);
        var clouds = new List<PointCloud>(index - first + 1);
        for (int i = first; i <= index; i++)
            clouds.Add(Sequence.LoadFrame(i));

        Pipeline.Source = CloudMerge.Concatenate(clouds);
        PointCloud = Pipeline.Output;
        OnPropertyChanged(nameof(FrameInfo));
        OnPropertyChanged(nameof(FrameNumber));
    }

    public void Advance(TimeSpan delta)
    {
        if (Player is null || !Player.IsPlaying)
            return;

        Player.Advance(delta);

        if (!Player.IsPlaying)
            OnPropertyChanged(nameof(IsPlaying));

        if (!IsScrubbing)
            PositionSeconds = Player.PositionSeconds;
    }

    partial void OnPositionSecondsChanged(double value)
    {
        if (Player is null || Math.Abs(value - Player.PositionSeconds) < 1e-9)
            return;

        Seek(value);
    }

    public void Seek(double seconds)
    {
        if (Player is null)
            return;

        Player.Seek(seconds);
        PositionSeconds = Player.PositionSeconds;
        OnPropertyChanged(nameof(FrameInfo));
    }
}
