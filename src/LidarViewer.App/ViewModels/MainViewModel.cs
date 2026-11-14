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

    // Zakres kolorow ma trzy stany: domyslny (0..1, tylko placeholder),
    // policzony z danych oraz ustawiony recznie przez uzytkownika.
    // Roznica miedzy ostatnimi dwoma jest istotna - domyslny zakres nie moze
    // byc traktowany jak wybor uzytkownika, bo chmura wyswietlalaby sie
    // jednokolorowo bez zadnego ostrzezenia.
    private bool _rangeUserSet;
    private bool _rangeSeeded;
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
        ColorMode.Intensity => "Intensywnosc",
        ColorMode.Distance => "Odleglosc od sensora",
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
                return "Ten plik nie zawiera atrybutu intensity - punkty sa pokazane na szaro.";

            if (ColorMode == ColorMode.Solid)
                return null;

            if (ColorSettings.IsDegenerate)
                return "Zakres kolorow jest pusty (min >= max) - wszystkie punkty maja jeden kolor.";

            var attribute = PointCloud?.GetAttribute(CurrentScalarKind);
            if (attribute is not null && (attribute.Min > ColorRangeMax || attribute.Max < ColorRangeMin))
                return "Zakres kolorow lezy poza danymi - wszystkie punkty maja jeden kolor.";

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
            Status = $"Wczytano {cloud.Count:N0} punktów ({System.IO.Path.GetFileName(path)}).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    partial void OnPointCloudChanged(PointCloud? value)
    {
        // Nowy plik ma inne wartosci danych, wiec zakres policzony dla
        // poprzedniego pliku przestaje obowiazywac. Zakres wpisany recznie
        // przez uzytkownika zostaje - to swiadome ustawienie, a nie
        // wyliczenie z danych.
        _rangeSeeded = false;
        ApplyRangeForMode();
    }

    partial void OnColorModeChanged(ColorMode value) => ApplyRangeForMode();

    partial void OnAutoColorRangeChanged(bool value)
    {
        if (value)
        {
            _rangeUserSet = false;
            ApplyRangeForMode();
            return;
        }

        // Wylaczenie trybu automatycznego zamraza biezacy zakres, ale tylko
        // wtedy, gdy pochodzi on z danych. Domyslne 0..1 to placeholder,
        // a nie wybor uzytkownika - pozostawienie go zamroczonym dalo
        // jednokolorowa chmure bez ostrzezenia.
        if (_rangeSeeded)
            _rangeUserSet = true;
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

        _rangeUserSet = true;

        if (AutoColorRange)
            AutoColorRange = false;
    }

    // Wybor trybu kolorowania ustawia zakres na dane, inaczej chmura
    // zostalaby jednokolorowa bez zadnego ostrzezenia. Zakres wprowadzony
    // recznie przez uzytkownika pozostaje nietkniety.
    private void ApplyRangeForMode()
    {
        if (PointCloud is null || ColorMode == ColorMode.Solid)
            return;

        var attribute = PointCloud.GetAttribute(CurrentScalarKind);
        if (attribute is null)
            return;

        // Zakres wpisany recznie przez uzytkownika jest jego decyzja i nie
        // moze zostac podmieniony - nawet wtedy, gdy jest pusty (min == max)
        // albo odwrocony. Uzytkownik dostanie wtedy ostrzezenie zamiast
        // cichego podmienienia na zakres z danych.
        if (!AutoColorRange && _rangeUserSet)
            return;

        SetRange(attribute.Min, attribute.Max);
    }

    // Zakres ustawiamy w kolejnosci, ktora nigdy nie publikuje odwroconego
    // zakresu (nowe Min przy starym Max) - chmura flashowaby wtedy jednym kolorem.
    private void SetRange(float min, float max)
    {
        _updatingRange = true;
        try
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
        finally
        {
            _updatingRange = false;
        }

        _rangeSeeded = true;
    }
}
