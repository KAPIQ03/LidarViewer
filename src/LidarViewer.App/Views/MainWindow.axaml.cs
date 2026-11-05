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

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz plik z chmurą punktów",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Chmury punktów")
                    { Patterns = PointCloudLoader.SupportedExtensions.Select(e => "*" + e).ToArray() },
                FilePickerFileTypes.All,
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            vm.FilePath = path;
            await vm.LoadCommand.ExecuteAsync(null);
        }
    }
}
