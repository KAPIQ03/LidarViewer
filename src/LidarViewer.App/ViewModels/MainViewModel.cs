using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.Core.Models;
using LidarViewer.Core.Readers;

namespace LidarViewer.App.ViewModels;

/// <summary>
/// Model widoku głównego okna (Faza 1): ścieżka pliku .vtp, komenda wczytania
/// i wczytana chmura punktów, którą widok 3D wiąże (binding) i rysuje.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Ścieżka do pliku .vtp wpisana przez użytkownika.</summary>
    [ObservableProperty]
    public partial string FilePath { get; set; } = "";

    /// <summary>Aktualnie wyświetlana chmura punktów (null = nic nie wczytano).</summary>
    [ObservableProperty]
    public partial PointCloud? PointCloud { get; set; }

    /// <summary>Płaszczyzna rzutu chmury w widoku 2D (wybierana z listy).</summary>
    [ObservableProperty]
    public partial ProjectionPlane Plane { get; set; } = ProjectionPlane.XY;

    /// <summary>Tryb widoku: 3D (OpenGL) albo płaski rzut 2D.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Is3D), nameof(Is2D))]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Widok3D;

    /// <summary>Dostępne tryby widoku - źródło listy rozwijanej.</summary>
    public ViewMode[] ViewModes { get; } = Enum.GetValues<ViewMode>();

    public bool Is3D => ViewMode == ViewMode.Widok3D;
    public bool Is2D => ViewMode == ViewMode.Widok2D;

    /// <summary>Dostępne płaszczyzny rzutu - źródło listy rozwijanej.</summary>
    public ProjectionPlane[] Planes { get; } = Enum.GetValues<ProjectionPlane>();

    /// <summary>Dziennik diagnostyczny OpenGL ustawiany przez sondę/widok 3D (wersja GL, błędy).</summary>
    [ObservableProperty]
    public partial string? Diagnostics { get; set; } = "OpenGL: czekam na inicjalizację kontekstu...";

    /// <summary>Komunikat o stanie: wynik wczytania albo treść błędu.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Podaj ścieżkę do pliku .vtp.";

    /// <summary>
    /// Wczytuje plik poza wątkiem UI (parsowanie ~11 MB nie może blokować okna),
    /// po czym ustawia PointCloud, co odświeża widok 3D.
    /// </summary>
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
        catch (Exception ex) // błąd wczytania nie może wywrócić aplikacji - pokazujemy go w pasku stanu
        {
            Status = $"Błąd: {ex.Message}";
        }
    }
}
