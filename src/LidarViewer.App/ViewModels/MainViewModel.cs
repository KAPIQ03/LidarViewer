using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.App.Streaming;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Readers;
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

    [ObservableProperty]
    public partial PointCloud? SourceCloud { get; set; }

    public PipelineViewModel Pipeline { get; } = new();


    [ObservableProperty]
    public partial ProjectionPlane Plane { get; set; } = ProjectionPlane.XY;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Is3D), nameof(Is2D))]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Widok3D;

    public ViewMode[] ViewModes { get; } = Enum.GetValues<ViewMode>();

    public bool Is3D => ViewMode == ViewMode.Widok3D;

    public bool ShowViewCube => Is3D;
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
        ColorMode.Height => "Wysokość (Z)",
        ColorMode.LaserId => "Numer lasera",
        ColorMode.Azimuth => "Azymut [°]",
        ColorMode.Time => "Czas w klatce [s]",
        _ => "Jednolity",
    };

    public bool HasIntensity => PointCloud?.GetAttribute(ScalarKind.Intensity) is not null;

    private ScalarKind CurrentScalarKind => ColorMode.ToScalarKind() ?? ScalarKind.Distance;

    public string? ColorWarning
    {
        get
        {
            if (ColorMode == ColorMode.Intensity && !HasIntensity)
                return "Ten plik nie zawiera atrybutu intensity - punkty są pokazane na szaro.";

            if (ColorMode == ColorMode.Solid)
                return null;

            if (PointCloud is not null && PointCloud.GetAttribute(CurrentScalarKind) is null)
                return $"Te dane nie zawierają atrybutu „{LegendTitle}” - punkty są pokazane na szaro.";

            if (ColorSettings.IsDegenerate)
                return "Zakres kolorów jest pusty (min >= max) - wszystkie punkty mają jeden kolor.";

            var attribute = PointCloud?.GetAttribute(CurrentScalarKind);
            if (attribute is not null && (attribute.Min > ColorRangeMax || attribute.Max < ColorRangeMin))
                return "Zakres kolorów leży poza danymi - wszystkie punkty mają jeden kolor.";

            return null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalibrationLabel))]
    public partial string CalibrationPath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalibrationLabel))]
    public partial VelodyneModel SensorModel { get; set; } = VelodyneModel.Vlp16;

    public VelodyneModel[] SensorModels { get; } = Enum.GetValues<VelodyneModel>();

    public string CalibrationLabel => string.IsNullOrWhiteSpace(CalibrationPath)
        ? $"Kalibracja: {BuiltInCalibration.Name} (wbudowana)"
        : "Kalibracja: " + System.IO.Path.GetFileName(CalibrationPath);

    public IRelayCommand ResetCalibrationCommand { get; }

    private VelodyneCalibration LoadCalibration() =>
        string.IsNullOrWhiteSpace(CalibrationPath)
            ? BuiltInCalibration
            : VelodyneCalibrationReader.Read(CalibrationPath);

    private VelodyneCalibration BuiltInCalibration => SensorModel == VelodyneModel.Hdl32E
        ? VelodyneCalibration.Hdl32E
        : VelodyneCalibration.Vlp16;

    [ObservableProperty]
    public partial double PointSize { get; set; } = 2.0;

    [ObservableProperty]
    public partial bool ShowGrid { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowAxes { get; set; } = true;

    [ObservableProperty]
    public partial string Status { get; set; } = "Podaj ścieżkę do pliku .vtp.";

    public bool AutoFitCamera => !IsLive && !HasSequence;

    [ObservableProperty]
    public partial int FitVersion { get; set; }

    public bool AutoResetView => !IsLive && !HasSequence;

    [ObservableProperty]
    public partial bool IsFiltersPanelOpen { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDiagnosticsOpen { get; set; }


    public MainViewModel()
    {
        Pipeline.PropertyChanged += OnPipelinePropertyChanged;
        PlayPauseCommand = new RelayCommand(PlayPauseCore);
        StartLiveCommand = new RelayCommand(StartLiveCore);
        StopLiveCommand = new RelayCommand(StopLiveCore);
        ResetCalibrationCommand = new RelayCommand(() => CalibrationPath = "");
        Speed = Speeds.First(speed => speed.Multiplier == 1.0);
    }

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

        if (Pipeline.Output is not null && !IsLive)
            Status = $"Po filtrach: {Pipeline.Output.Count:N0} punktów.";
    }

    public string StatusInfo
    {
        get
        {
            string points = $"Punkty: {PointCloud?.Count ?? 0:N0}";
            if (IsLive)
                return points + " | LIVE" + LiveRates;
            return Sequence is not null ? points + $" | Sekwencja: {Sequence.Name}" : points;
        }
    }

    [ObservableProperty]
    public partial bool IsMeasuring { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MeasureInfo))]
    public partial double? MeasuredDistance { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MeasureInfo))]
    public partial int MeasuredPointCount { get; set; }

    public string MeasureInfo => !IsMeasuring ? string.Empty
        : MeasuredDistance is { } distance ? $"Odległość: {distance:0.000}"
        : MeasuredPointCount == 1 ? "Wybierz drugi punkt."
        : "Kliknij punkt chmury.";

    partial void OnIsMeasuringChanged(bool value) => OnPropertyChanged(nameof(MeasureInfo));

    [ObservableProperty]
    public partial bool IsTableOpen { get; set; }

    [ObservableProperty]
    public partial PointTable? Table { get; set; }

    partial void OnIsTableOpenChanged(bool value) => RefreshTable();

    private void RefreshTable() =>
        Table = IsTableOpen && PointCloud is { } cloud ? new PointTable(cloud) : null;

    partial void OnPointCloudChanged(PointCloud? value)
    {
        RefreshTable();
        OnPropertyChanged(nameof(StatusInfo));

        _rangeComputed = false;
        ApplyRangeForFile();
    }

    partial void OnColorModeChanged(ColorMode value) => ApplyRangeForMode();

    partial void OnAutoColorRangeChanged(bool value)
    {
        if (!value)
            return;

        _rangeManual = false;
        ApplyRangeForMode();
    }

    partial void OnColorRangeMinChanged(float value) => OnManualRangeEdit();

    partial void OnColorRangeMaxChanged(float value) => OnManualRangeEdit();

    private void OnManualRangeEdit()
    {
        if (_updatingRange)
            return;

        _rangeManual = true;
        _rangeComputed = false;

        if (AutoColorRange)
            AutoColorRange = false;
    }

    private void ApplyRangeForMode()
    {
        if (PointCloud is null || ColorMode == ColorMode.Solid)
            return;

        if (_rangeManual)
            return;

        if (!AutoColorRange && _rangeComputed)
            return;

        SeedRangeFromData();
    }

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
