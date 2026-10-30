using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LidarViewer.App.ViewModels;

namespace LidarViewer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private async void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wczytaj chmurę punktów",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("VTP i CSV") { Patterns = new[] { "*.vtp", "*.csv" } },
                new FilePickerFileType("VTP") { Patterns = new[] { "*.vtp" } },
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } }
            }
        });

        if (files.Count > 0 && DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadFileAsync(files[0].Path.LocalPath);
        }
    }

    private async void StartRecording_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz nagranie LIDAR",
            SuggestedFileName = $"lidar-recording-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Nagranie punktów CSV") { Patterns = new[] { "*.csv" } }
            }
        });

        if (file is not null && DataContext is MainViewModel viewModel)
        {
            await viewModel.StartRecordingAsync(file.Path.LocalPath);
        }
    }

    private void ResetCamera_Click(object? sender, RoutedEventArgs e)
    {
        this.FindControl<PointCloudView>("CloudView")?.ResetCamera();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.DisposeAsync();
        }
    }
}