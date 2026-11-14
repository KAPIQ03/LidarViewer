using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.Core.Models;
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
        ColorMode.Intensity => "Intensywnosc",
        ColorMode.Distance => "Odleglosc od sensora",
        _ => "Jednolity",
    };

    public bool HasIntensity => PointCloud?.GetAttribute(ScalarKind.Intensity) is not null;

    public string? ColorWarning
    {
        get
        {
            if (ColorMode == ColorMode.Intensity && !HasIntensity)
                return "Ten plik nie zawiera atrybutu intensity - punkty sa pokazane na szaro.";

            if (ColorMode != ColorMode.Solid && ColorSettings.IsDegenerate)
                return "Zakres kolorow jest pusty (min >= max) - wszystkie punkty maja jeden kolor.";

            return null;
        }
    }

    [ObservableProperty]
    public partial string Status { get; set; } = "Podaj ścieżkę do pliku .vtp.";

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
            PointCloud = cloud;
            ApplyAutoRange();
            Status = $"Wczytano {cloud.Count:N0} punktów ({System.IO.Path.GetFileName(path)}).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    partial void OnColorModeChanged(ColorMode value) => ApplyRangeForMode();

    partial void OnAutoColorRangeChanged(bool value) => ApplyAutoRange();

    private void ApplyAutoRange()
    {
        if (!AutoColorRange || PointCloud is null)
            return;

        ApplyRangeForMode();
    }

    // Wybor trybu kolorowania ustawia zakres na dane, inaczej chmura
    // zostalaby jednokolorowa bez zadnego ostrzezenia. Zakres wprowadzony
    // recznie przez uzytkownika pozostaje nietkniety, dopoki nie jest pusty.
    private void ApplyRangeForMode()
    {
        if (PointCloud is null || ColorMode == ColorMode.Solid)
            return;

        ScalarKind kind = ColorMode == ColorMode.Intensity
            ? ScalarKind.Intensity
            : ScalarKind.Distance;

        var attribute = PointCloud.GetAttribute(kind);
        if (attribute is null)
            return;

        if (!AutoColorRange && ColorRangeMax > ColorRangeMin)
            return;

        SetRange(attribute.Min, attribute.Max);
    }

    // Zakres ustawiamy w kolejnosci, ktora nigdy nie publikuje odwroconego
    // zakresu (nowe Min przy starym Max) - chmura flashowaby wtedy jednym kolorem.
    private void SetRange(float min, float max)
    {
        if (min > ColorRangeMax)
        {
            ColorRangeMax = max;
            ColorRangeMin = min;
        }
        else
        {
            ColorRangeMin = min;
            ColorRangeMax = max;
        }
    }
}
