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


    [ObservableProperty]
    public partial ProjectionPlane Plane { get; set; } = ProjectionPlane.XY;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Is3D), nameof(Is2D), nameof(IsSensorView))]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Widok3D;

    public ViewMode[] ViewModes { get; } = Enum.GetValues<ViewMode>();

    /// <summary>Widok OpenGL jest wspolny dla trybow 3D i Kamera.</summary>
    public bool Is3D => ViewMode is ViewMode.Widok3D or ViewMode.Kamera;

    /// <summary>Tryb Kamera: widok z perspektywy lidaru, obracany myszka.</summary>
    public bool IsSensorView => ViewMode == ViewMode.Kamera;
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

    /// <summary>
    /// Plik kalibracji Velodyne (db.xml). Pusty = wbudowana kalibracja VLP-16. Uzywany przy
    /// otwieraniu nagran pcap i w trybie live Velodyne.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalibrationLabel))]
    public partial string CalibrationPath { get; set; } = "";

    public string CalibrationLabel => string.IsNullOrWhiteSpace(CalibrationPath)
        ? "Kalibracja: VLP-16 (wbudowana)"
        : "Kalibracja: " + System.IO.Path.GetFileName(CalibrationPath);

    public IRelayCommand ResetCalibrationCommand { get; }

    private VelodyneCalibration LoadCalibration() =>
        string.IsNullOrWhiteSpace(CalibrationPath)
            ? VelodyneCalibration.Vlp16
            : VelodyneCalibrationReader.Read(CalibrationPath);

    [ObservableProperty]
    public partial string Status { get; set; } = "Podaj ścieżkę do pliku .vtp.";

    /// <summary>
    /// Czy widok ma sam dopasowac kamere do chmury. W trybie live kamera
    /// zostaje na miejscu, zeby uzytkownik mogl obracac chmure recznie.
    /// </summary>
    public bool AutoFitCamera => !IsLive && !HasSequence;

    /// <summary>
    /// Zwieksza sie przy kazdym nowym zbiorze danych (plik, sekwencja). Widok 3D
    /// dopasowuje wtedy kamere raz, a kolejne klatki sekwencji jej nie ruszaja.
    /// </summary>
    [ObservableProperty]
    public partial int FitVersion { get; set; }

    /// <summary>
    /// Czy widok 2D ma sam resetowac zoom i przesuniecie. W trybie live
    /// widok zostaje na miejscu.
    /// </summary>
    public bool AutoResetView => !IsLive && !HasSequence;

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


    public MainViewModel()
    {
        Pipeline.PropertyChanged += OnPipelinePropertyChanged;
        PlayPauseCommand = new RelayCommand(PlayPauseCore);
        LoadSequenceCommand = new RelayCommand<string>(LoadSequenceCore);
        StartLiveCommand = new RelayCommand(StartLiveCore);
        StopLiveCommand = new RelayCommand(StopLiveCore);
        ResetCalibrationCommand = new RelayCommand(() => CalibrationPath = "");
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

        if (IsPcapFile(path))
        {
            await LoadPcapAsync(path);
            return;
        }

        Status = "Wczytywanie...";
        try
        {
            var cloud = await Task.Run(() => PointCloudLoader.Read(path));
            CloseSequence();
            ApplySource(cloud);
            Status = $"Wczytano {cloud.Count:N0} punktów ({System.IO.Path.GetFileName(path)}).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
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
