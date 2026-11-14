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

    /// <summary>
    /// Pojemnosc bufora punktow w trybie live. Wieksza wartosc daje
    /// dluzsza historie, ale wolniejsze przeladowanie widoku.
    /// </summary>
    public const int LiveBufferCapacity = 200_000;

    private UdpCloudReceiver? _receiver;
    private LiveSimulator? _simulator;
    private bool _isLive;

    [ObservableProperty]
    public partial int LivePort { get; set; } = 2368;

    [ObservableProperty]
    public partial LiveProtocol LiveProtocol { get; set; } = LiveProtocol.Wlasny;

    public LiveProtocol[] LiveProtocols { get; } = Enum.GetValues<LiveProtocol>();

    /// <summary>
    /// Czy odbior UDP pracuje.
    /// </summary>
    public bool IsLive
    {
        get => _isLive;
        private set
        {
            if (_isLive == value)
                return;

            _isLive = value;
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(AutoFitCamera));
            OnPropertyChanged(nameof(AutoResetView));
            OnPropertyChanged(nameof(LiveInfo));
            OnPropertyChanged(nameof(CanStartRecording));
        }
    }

    /// <summary>
    /// Czy widok ma sam dopasowac kamere do chmury. W trybie live kamera
    /// zostaje na miejscu, zeby uzytkownik mogl obracac chmure recznie.
    /// </summary>
    public bool AutoFitCamera => !IsLive;

    /// <summary>
    /// Czy widok 2D ma sam resetowac zoom i przesuniecie. W trybie live
    /// widok zostaje na miejscu.
    /// </summary>
    public bool AutoResetView => !IsLive;

    /// <summary>
    /// Czy panel filtrow jest widoczny. Panel mozna zamknac, zeby nie
    /// zaslanial widoku chmury.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFiltersPanelOpen { get; set; } = true;

    /// <summary>
    /// Czy okno diagnostyki jest widoczne. Domyslnie zamkniete, bo to
    /// pomocnicze okno, ktore zaslanialo dolna czesc widoku.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDiagnosticsOpen { get; set; }


    [ObservableProperty]
    public partial bool SimulatorEnabled { get; set; }

    public int LivePointCount => _receiver?.Buffer.Count ?? 0;

    public long LivePacketCount => _receiver?.PacketCount ?? 0;

    public long LiveRejectedCount => _receiver?.RejectedCount ?? 0;

    /// <summary>
    /// Podsumowanie strumienia pokazane przy pasku live.
    /// </summary>
    public string LiveInfo => _receiver is null
        ? string.Empty
        : $"Punkty: {LivePointCount:N0} | Pakiety: {LivePacketCount:N0} | Odrzucone: {LiveRejectedCount:N0}";

    public IRelayCommand StartLiveCommand { get; }

    public IRelayCommand StopLiveCommand { get; }

    private SequenceRecorder? _recorder;

    /// <summary>
    /// Odstep miedzy nagranymi klatkami w sekundach. Klatka to cala chmura
    /// widoczna w danym momencie, wiec jedna na sekunde daje zapis podobny
    /// do filmu zamiast zalewy plikow.
    /// </summary>
    public double RecordingIntervalSeconds { get; set; } = 1.0;

    /// <summary>
    /// Czy trwa nagrywanie strumienia live do pliku.
    /// </summary>
    public bool IsRecording
    {
        get => _recorder is not null;
    }

    /// <summary>
    /// Czy uzytkownik moze zaczac nowe nagrywanie. Nagrywanie wymaga
    /// strumienia live i nie moze startowac dwa razy.
    /// </summary>
    public bool CanStartRecording => IsLive && !IsRecording;

    /// <summary>
    /// Liczba klatek przyjetych do nagrania.
    /// </summary>
    public int RecordedFrameCount => _recorder?.FrameCount ?? 0;

    public string RecordInfo => IsRecording
        ? $"Nagrane klatki: {RecordedFrameCount:N0}"
        : string.Empty;

    /// <summary>
    /// Zapisuje chmure widoczna w scenie - czyli juz przepuszczona przez
    /// filtry - do pliku. Format wybiera sie po rozszerzeniu.
    /// </summary>
    public void ExportCloud(string path)
    {
        if (PointCloud is null)
        {
            Status = "Błąd: nie ma chmury do zapisania.";
            return;
        }

        try
        {
            PointCloudWriter.Write(PointCloud, path);
            Status = $"Zapisano {PointCloud.Count:N0} punktów do {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    /// <summary>
    /// Zaczyna nagrywanie strumienia live do katalogu. Katalog jest
    /// tworzony, a klatki dostaja pliki frame_00000.ply i kolejne.
    /// </summary>
    public void StartRecording(string directory)
    {
        if (_recorder is not null)
            return;

        try
        {
            _recorder = new SequenceRecorder(directory, null, RecordingIntervalSeconds);
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanStartRecording));
            OnPropertyChanged(nameof(RecordInfo));
            Status = $"Nagrywanie do katalogu {directory}.";
        }
        catch (Exception ex)
        {
            _recorder = null;
            Status = $"Błąd: {ex.Message}";
        }
    }

    /// <summary>
    /// Konczy nagrywanie i zapisuje manifest, dzieki czemu nagranie da sie
    /// odtworzyc w aplikacji.
    /// </summary>
    public async Task StopRecordingAsync()
    {
        if (_recorder is null)
            return;

        SequenceRecorder recorder = _recorder;
        _recorder = null;
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(CanStartRecording));

        try
        {
            string manifest = await recorder.FinishAsync();
            Status = $"Zapisano nagranie: {manifest} ({recorder.FrameCount} klatek).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
        finally
        {
            OnPropertyChanged(nameof(RecordInfo));
        }
    }

    /// <summary>
    /// Nowa chmura z odbiornika. Wywolywane z watku odbioru - widok
    /// przenosi je na watek interfejsu.
    /// </summary>
    public event Action<PointCloud>? LiveCloudUpdated;

    public MainViewModel()
    {
        Pipeline.PropertyChanged += OnPipelinePropertyChanged;
        PlayPauseCommand = new RelayCommand(PlayPauseCore);
        LoadSequenceCommand = new RelayCommand<string>(LoadSequenceCore);
        StartLiveCommand = new RelayCommand(StartLiveCore);
        StopLiveCommand = new RelayCommand(StopLiveCore);
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

    private void StartLiveCore()
    {
        if (_receiver is not null)
            return;

        try
        {
            IPacketDecoder decoder = LiveProtocol switch
            {
                LiveProtocol.VelodyneVlp16 => new VelodyneVlp16Decoder(),
                _ => new SimpleCloudDecoder(),
            };

            var receiver = new UdpCloudReceiver(LivePort, decoder, LiveBufferCapacity);
            receiver.CloudUpdated += ReceiveLiveCloud;
            receiver.Start();

            _receiver = receiver;
            IsLive = true;
            Status = $"Nasłuch UDP na porcie {receiver.Port} ({decoder.Name}).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    private void StopLiveCore()
    {
        if (_receiver is null)
            return;

        _receiver.CloudUpdated -= ReceiveLiveCloud;
        _receiver.Dispose();
        _receiver = null;
        IsLive = false;

        // Po zatrzymaniu odbioru wracamy do chmury z pliku - widok nie
        // powinien zostac z ostatnia stanu na zywo.
        PointCloud = Pipeline.Output;
        Status = "Zatrzymano odbiór na żywo.";
    }

    /// <summary>
    /// Nowa chmura z odbiornika. Wolane z watku odbioru - widok
    /// przenosi ja na watek interfejsu.
    /// </summary>
    public void ReceiveLiveCloud(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        // Klatka nagrywana jest tutaj, a nie w ApplyLiveCloud - strumien
        // dociera do odbioru niezaleznie od tego, czy okno jest otwarte.
        RecordLiveFrame(cloud);
        LiveCloudUpdated?.Invoke(cloud);
    }

    /// <summary>
    /// Wstawia chmure otrzymana z odbiornika do widoku. Wolane z watku
    /// interfejsu.
    /// </summary>
    public void ApplyLiveCloud(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        PointCloud = cloud;
        OnPropertyChanged(nameof(LivePointCount));
        OnPropertyChanged(nameof(LivePacketCount));
        OnPropertyChanged(nameof(LiveInfo));
        OnPropertyChanged(nameof(RecordInfo));
    }

    // Klatka nagrywana jest w chwili, gdy chmura trafia na ekran. Recorder
    // odrzuca klatki za szybkie, wiec zapis nie zalewa dysku.
    private void RecordLiveFrame(PointCloud cloud)
    {
        SequenceRecorder? recorder = _recorder;
        if (recorder is null)
            return;

        recorder.AddFrame(cloud);
    }

    partial void OnSimulatorEnabledChanged(bool value)
    {
        if (value)
        {
            // Symulator mowi wlasnym protokolem - inny dekoder odrzucilby
            // wszystkie pakiety.
            LiveProtocol = LiveProtocol.Wlasny;

            if (_receiver is null)
                StartLiveCore();

            if (_receiver is null)
            {
                SimulatorEnabled = false;
                return;
            }

            _simulator?.Dispose();
            _simulator = new LiveSimulator("127.0.0.1", _receiver.Port);
            _simulator.Start();
            Status = "Symulator włączony - pakiety lecą na 127.0.0.1.";
        }
        else
        {
            _simulator?.Dispose();
            _simulator = null;
        }
    }

    partial void OnLiveProtocolChanged(LiveProtocol value)
    {
        // Zmiana formatu pakietow wymaga nowego dekodera, wiec odbior
        // musi wystartowac od nowa.
        if (_receiver is null)
            return;

        _receiver.CloudUpdated -= ReceiveLiveCloud;
        _receiver.Dispose();
        _receiver = null;

        StartLiveCore();
    }
}
