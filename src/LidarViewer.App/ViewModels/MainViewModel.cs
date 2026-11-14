using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Readers;
using LidarViewer.Core.Rendering;

namespace LidarViewer.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string FilePath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorWarning))]
    [NotifyPropertyChangedFor(nameof(HasIntensity))]
    public partial PointCloud? PointCloud { get; set; }

    /// <summary>
    /// Wczytany plik - zrodlo pipeline. Punkty widoczne w scenie to wynik
    /// filtrow, a nie surowa chmura.
    /// </summary>
    [ObservableProperty]
    public partial PointCloud? SourceCloud { get; set; }

    public PipelineViewModel Pipeline { get; } = new();

    /// <summary>
    /// Wczytana sekwencja klatek. null, gdy aplikacja pokazuje pojedynczy
    /// plik.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequence))]
    [NotifyPropertyChangedFor(nameof(FrameInfo))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public partial Sequence? Sequence { get; set; }

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
        : $"Klatka {Player?.FrameIndex + 1} / {Sequence.Frames.Count}";

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

    [ObservableProperty]
    public partial ProjectionPlane Plane { get; set; } = ProjectionPlane.XY;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Is3D), nameof(Is2D))]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Widok3D;

    public ViewMode[] ViewModes { get; } = Enum.GetValues<ViewMode>();

    public bool Is3D => ViewMode == ViewMode.Widok3D;
    public bool Is2D => ViewMode == ViewMode.Widok2D;

    public ProjectionPlane[] Planes { get; } = Enum.GetValues<ProjectionPlane>();

    [ObservableProperty]
    public partial string? Diagnostics { get; set; } = "OpenGL: czekam na inicjalizację kontekstu...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    [NotifyPropertyChangedFor(nameof(LegendTitle))]
    [NotifyPropertyChangedFor(nameof(ShowLegend))]
    [NotifyPropertyChangedFor(nameof(ColorWarning))]
    public partial ColorMode ColorMode { get; set; } = ColorMode.Solid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    public partial ColormapKind Colormap { get; set; } = ColormapKind.Viridis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    public partial bool AutoColorRange { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    [NotifyPropertyChangedFor(nameof(ColorWarning))]
    public partial float ColorRangeMin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    [NotifyPropertyChangedFor(nameof(ColorWarning))]
    public partial float ColorRangeMax { get; set; } = 1f;

    public ColorMode[] ColorModes { get; } = Enum.GetValues<ColorMode>();

    public ColormapKind[] Colormaps { get; } = Enum.GetValues<ColormapKind>();

    // Zakres kolorow ma trzy stany: wartosc wpisana recznie przez
    // uzytkownika, wartosc policzona z danych oraz domyslne 0..1, ktore jest
    // tylko placeholderem. Roznica miedzy nimi jest istotna - placeholder nie
    // moze zostac uznany za wybor uzytkownika, bo chmura wyswietlalaby sie
    // jednokolorowo bez zadnego ostrzezenia.
    private bool _rangeManual;
    private bool _rangeComputed;
    private bool _updatingRange;

    public ColorSettings ColorSettings => new()
    {
        Mode = ColorMode,
        Map = Colormap,
        Min = ColorRangeMin,
        Max = ColorRangeMax,
    };

    public bool ShowLegend => ColorMode != ColorMode.Solid;

    public string LegendTitle => ColorMode switch
    {
        ColorMode.Intensity => "Intensywność",
        ColorMode.Distance => "Odległość od sensora",
        _ => "Jednolity",
    };

    public bool HasIntensity => PointCloud?.GetAttribute(ScalarKind.Intensity) is not null;

    private ScalarKind CurrentScalarKind =>
        ColorMode == ColorMode.Intensity ? ScalarKind.Intensity : ScalarKind.Distance;

    public string? ColorWarning
    {
        get
        {
            if (ColorMode == ColorMode.Intensity && !HasIntensity)
                return "Ten plik nie zawiera atrybutu intensity - punkty są pokazane na szaro.";

            if (ColorMode == ColorMode.Solid)
                return null;

            if (ColorSettings.IsDegenerate)
                return "Zakres kolorów jest pusty (min >= max) - wszystkie punkty mają jeden kolor.";

            var attribute = PointCloud?.GetAttribute(CurrentScalarKind);
            if (attribute is not null && (attribute.Min > ColorRangeMax || attribute.Max < ColorRangeMin))
                return "Zakres kolorów leży poza danymi - wszystkie punkty mają jeden kolor.";

            return null;
        }
    }

    [ObservableProperty]
    public partial string Status { get; set; } = "Podaj ścieżkę do pliku .vtp.";

    public MainViewModel()
    {
        Pipeline.PropertyChanged += OnPipelinePropertyChanged;
        PlayPauseCommand = new RelayCommand(PlayPauseCore);
        LoadSequenceCommand = new RelayCommand<string>(LoadSequenceCore);
        Speed = Speeds[2];
    }

    /// <summary>
    /// Jawne wejscie dla testow - produkcja wczytuje plik komenda Load.
    /// </summary>
    public void SetSourceForTests(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ApplySource(cloud);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        string path = FilePath.Trim().Trim('"');
        if (path.Length == 0)
        {
            Status = "Podaj ścieżkę do pliku .vtp.";
            return;
        }

        Status = "Wczytywanie...";
        try
        {
            var cloud = await Task.Run(() => PointCloudLoader.Read(path));
            ApplySource(cloud);
            Status = $"Wczytano {cloud.Count:N0} punktów ({System.IO.Path.GetFileName(path)}).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    private void ApplySource(PointCloud cloud)
    {
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
            var sequence = SequenceLoader.Load(directory);
            var player = new SequencePlayer(sequence) { Speed = Speed?.Multiplier ?? 1.0 };
            player.FrameChanged += OnFrameChanged;

            Sequence = sequence;
            Player = player;
            Pipeline.Reset();
            PositionSeconds = 0;
            ShowFrame(0);
            Status = $"Wczytano sekwencję „{sequence.Name}” ({sequence.Frames.Count} klatek).";
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

    public void Seek(double seconds)
    {
        if (Player is null)
            return;

        Player.Seek(seconds);
        PositionSeconds = Player.PositionSeconds;
        OnPropertyChanged(nameof(FrameInfo));
    }

    private void OnPipelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PipelineViewModel.Output))
            return;

        PointCloud = Pipeline.Output;

        if (Pipeline.Output is not null)
            Status = $"Po filtrach: {Pipeline.Output.Count:N0} punktów.";
    }

    partial void OnPointCloudChanged(PointCloud? value)
    {
        // Nowy plik ma inne wartosci danych, wiec zakres policzony dla
        // poprzedniego pliku przestaje obowiazywac. Zakres wpisany recznie
        // przez uzytkownika zostaje - to swiadome ustawienie, a nie
        // wyliczenie z danych.
        _rangeComputed = false;
        ApplyRangeForFile();
    }

    partial void OnColorModeChanged(ColorMode value) => ApplyRangeForMode();

    partial void OnAutoColorRangeChanged(bool value)
    {
        // Wylaczenie trybu automatycznego nie jest wyborem zakresu - to
        // jedynie zamrozenie biezacej wartosci. O tym, czy zakres zostal
        // wpisany recznie, decyduje wylacznie OnManualRangeEdit.
        if (!value)
            return;

        _rangeManual = false;
        ApplyRangeForMode();
    }

    partial void OnColorRangeMinChanged(float value) => OnManualRangeEdit();

    partial void OnColorRangeMaxChanged(float value) => OnManualRangeEdit();

    // Reczna zmiana min lub max oznacza wybor uzytkownika, wiec zakres
    // przestaje byc automatyczny. Inaczej wpisana wartosc zostalaby
    // nadpisana przy zmianie trybu kolorowania.
    private void OnManualRangeEdit()
    {
        if (_updatingRange)
            return;

        _rangeManual = true;
        _rangeComputed = false;

        if (AutoColorRange)
            AutoColorRange = false;
    }

    // Wybor trybu kolorowania ustawia zakres na dane, inaczej chmura
    // zostalaby jednokolorowa bez zadnego ostrzezenia.
    //
    // Tryb reczny zamraza zakres wylacznie wtedy, gdy wartosc pochodzi
    // z danych. Placeholder 0..1 musi zostac podmieniony w kazdym
    // przypadku, bo inaczej chmura wyswietlalaby sie jednokolorowo.
    private void ApplyRangeForMode()
    {
        if (PointCloud is null || ColorMode == ColorMode.Solid)
            return;

        // Zakres wpisany recznie przez uzytkownika jest jego decyzja i nie
        // moze zostac podmieniony - nawet wtedy, gdy jest pusty (min == max)
        // albo odwrocony. Uzytkownik dostanie wtedy ostrzezenie zamiast
        // cichego podmienienia na zakres z danych.
        if (_rangeManual)
            return;

        if (!AutoColorRange && _rangeComputed)
            return;

        SeedRangeFromData();
    }

    // Wczytanie nowego pliku uniewaznia zakres policzony dla poprzedniego,
    // bo dane moga miec inny zakres wartosci. Zakres wpisany recznie przez
    // uzytkownika zostaje nietkniety.
    private void ApplyRangeForFile()
    {
        if (PointCloud is null || ColorMode == ColorMode.Solid)
            return;

        if (_rangeManual)
            return;

        SeedRangeFromData();
    }

    private void SeedRangeFromData()
    {
        var attribute = PointCloud?.GetAttribute(CurrentScalarKind);
        if (attribute is null)
            return;

        _updatingRange = true;
        try
        {
            // Zakres ustawiamy w kolejnosci, ktora nigdy nie publikuje
            // odwroconego zakresu (nowe Min przy starym Max) - chmura
            // flashowaby wtedy jednym kolorem.
            if (attribute.Min > ColorRangeMax)
            {
                ColorRangeMax = attribute.Max;
                ColorRangeMin = attribute.Min;
            }
            else
            {
                ColorRangeMin = attribute.Min;
                ColorRangeMax = attribute.Max;
            }
        }
        finally
        {
            _updatingRange = false;
        }

        _rangeComputed = true;
    }
}
