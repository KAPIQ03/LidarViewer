using System.Linq;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using LidarViewer.App.ViewModels;
using LidarViewer.Core.Readers;

namespace LidarViewer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Otwiera systemowe okno wyboru pliku (filtr *.vtp) i wpisuje wybraną ścieżkę do ViewModelu.
    /// Okno dialogowe wymaga dostępu do okna (StorageProvider), dlatego obsługa jest tutaj,
    /// a nie w ViewModelu.
    /// </summary>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz plik z chmurą punktów",
            AllowMultiple = false,
            FileTypeFilter =
            [
                // Filtr budowany z listy obsługiwanych rozszerzeń, więc nowe formaty pojawią się same.
                new FilePickerFileType("Chmury punktów")
                    { Patterns = PointCloudLoader.SupportedExtensions.Select(e => "*" + e).ToArray() },
                FilePickerFileTypes.All,
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            vm.FilePath = path;
            await vm.LoadCommand.ExecuteAsync(null); // od razu wczytaj wybrany plik
        }
    }
}
