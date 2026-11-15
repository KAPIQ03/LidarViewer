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
    /// <summary>
    /// Wczytana sekwencja klatek. null, gdy aplikacja pokazuje pojedynczy
    /// plik.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequence))]
    [NotifyPropertyChangedFor(nameof(AutoFitCamera))]
    [NotifyPropertyChangedFor(nameof(AutoResetView))]
    [NotifyPropertyChangedFor(nameof(FrameInfo))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public partial IFrameSource? Sequence { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequence))]
    [NotifyPropertyChangedFor(nameof(FrameInfo))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public partial SequencePlayer? Player { get; set; }

    /// <summary>
    /// Pozycja na osi czasu w sekundach.
    /// </summary>
    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    public bool HasSequence => Sequence is not null;

    public bool IsPlaying => Player?.IsPlaying ?? false;

    public double Duration => Sequence?.Duration ?? 0;

    public string FrameInfo => Sequence is null
        ? string.Empty
        : $"Klatka {Player?.FrameIndex + 1} / {Sequence.FrameCount}";

    /// <summary>
    /// Czy uzytkownik trzyma suwak osi czasu. Wtedy timer nie rusza pozycji,
    /// bo inaczej kciuk uciekalby spod kursora.
    /// </summary>
    public bool IsScrubbing { get; set; }

    public IReadOnlyList<PlaybackSpeed> Speeds { get; } =
    [
        new PlaybackSpeed(0.25, "0.25x"),
        new PlaybackSpeed(0.5, "0.5x"),
        new PlaybackSpeed(1.0, "1x"),
        new PlaybackSpeed(2.0, "2x"),
        new PlaybackSpeed(4.0, "4x"),
    ];

    [ObservableProperty]
    public partial PlaybackSpeed? Speed { get; set; }

    public IRelayCommand PlayPauseCommand { get; }

    public IRelayCommand<string> LoadSequenceCommand { get; }

    private static bool IsPcapFile(string path)
    {
        string extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".pcap", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pcapng", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Plik pcap z Velodyne jest indeksowany raz, a klatki dekodowane leniwie - bez plikow posrednich.
    /// </summary>
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

    /// <summary>
    /// Zamyka biezaca sekwencje, zeby Play nie odtwarzal wczesniejszego pliku
    /// po wczytaniu innego zrodla.
    /// </summary>
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

    /// <summary>
    /// Zaczyna odtwarzanie podanego zrodla klatek od poczatku.
    /// </summary>
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
            OnPropertyChanged(nameof(IsPlaying));
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

        PointCloud cloud = Sequence.LoadFrame(index);
        Pipeline.Source = cloud;
        PointCloud = Pipeline.Output;
        OnPropertyChanged(nameof(FrameInfo));
    }

    /// <summary>
    /// Przesuwa odtwarzanie. Wolane z timera okna.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        if (Player is null || !Player.IsPlaying)
            return;

        Player.Advance(delta);

        // Odtwarzacz zatrzymuje sie sam na koncu sekwencji - bez tego
        // powiadomienia timer w oknie krecilby sie dalej na prozno.
        if (!Player.IsPlaying)
            OnPropertyChanged(nameof(IsPlaying));

        if (!IsScrubbing)
            PositionSeconds = Player.PositionSeconds;
    }

    /// <summary>
    /// Przesuniecie suwaka przez uzytkownika (takze strzalkami) przewija sekwencje od razu.
    /// Zmiany pochodzace z odtwarzacza maja te sama wartosc co jego pozycja, wiec sa pomijane.
    /// </summary>
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
