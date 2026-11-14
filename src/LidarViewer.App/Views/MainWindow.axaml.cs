using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LidarViewer.App.ViewModels;
using LidarViewer.Core.Models;
using LidarViewer.Core.Readers;

namespace LidarViewer.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DateTime _lastTick = DateTime.UtcNow;
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += OnTick;
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
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

    private async void OnBrowseSequenceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz katalog sekwencji",
        });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.LoadSequenceCommand.Execute(path);
    }

    private void OnScrubStart(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsScrubbing = true;
    }

    private void OnScrubEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.IsScrubbing = false;
        if (sender is Slider slider)
            vm.Seek(slider.Value);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.LiveCloudUpdated -= OnLiveCloudUpdated;
        }

        if (DataContext is not MainViewModel vm)
        {
            _vm = null;
            return;
        }

        _vm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.LiveCloudUpdated += OnLiveCloudUpdated;
    }

    // Chmura przychodzi z watku odbioru UDP, wiec trzeba ja przeniesc na
    // watek interfejsu - wlasniki widoku nie wolno zmieniac z innego watku.
    private void OnLiveCloudUpdated(PointCloud cloud) =>
        Dispatcher.UIThread.Post(() => _vm?.ApplyLiveCloud(cloud));

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlaying))
            _timer.IsEnabled = (DataContext as MainViewModel)?.IsPlaying ?? false;
    }

    // Zamkniecie okna nie moze zostawic odbiornika UDP na zywo.
    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _vm?.SimulatorEnabled = false;
        _vm?.StopLiveCommand.Execute(null);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        DateTime now = DateTime.UtcNow;
        TimeSpan delta = now - _lastTick;
        _lastTick = now;

        vm.Advance(delta);
    }
}
