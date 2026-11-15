using System;
using System.ComponentModel;
using System.IO;
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

        // Slider sam obsluguje wskaznik i oznacza zdarzenia jako obsluzone, wiec zwykle
        // atrybuty XAML nigdy nie dostawaly PointerPressed/Released. Tunel + handledEventsToo
        // lapie poczatek i koniec przeciagania.
        SeekSlider.AddHandler(PointerPressedEvent, OnScrubStart, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, OnScrubEnd, RoutingStrategies.Bubble | RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, OnScrubEnd, RoutingStrategies.Bubble | RoutingStrategies.Tunnel, handledEventsToo: true);

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
                // Pierwszy filtr jest domyslny - musi obejmowac tez pcap, inaczej natywne
                // okno (macOS) szarzy takie pliki, dopoki uzytkownik nie przelaczy filtra.
                new FilePickerFileType("Chmury punktów i nagrania pcap")
                    { Patterns = PointCloudLoader.SupportedExtensions.Concat([".pcap", ".pcapng"]).Select(e => "*" + e).ToArray() },
                new FilePickerFileType("Nagranie Velodyne VLP-16 (pcap)") { Patterns = ["*.pcap", "*.pcapng"] },
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

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz chmurę punktów",
            SuggestedFileName = "chmura.ply",
            FileTypeChoices =
            [
                new FilePickerFileType("Chmura PLY") { Patterns = ["*.ply"] },
                new FilePickerFileType("Punkty tekstowe") { Patterns = ["*.xyz", "*.csv"] },
            ],
        });

        if (file?.TryGetLocalPath() is { } path)
            vm.ExportCloud(path);
    }

    private async void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz katalog nagrania",
        });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } parent)
            return;

        // Kazde nagranie dostaje wlasny podkatalog, wiec kolejne nagrania
        // nie nadpisuja poprzedniego.
        string target = Path.Combine(parent, $"nagranie-{DateTime.Now:yyyyMMdd-HHmmss}");
        vm.StartRecording(target);
    }

    private async void OnStopRecordingClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        await vm.StopRecordingAsync();
    }

    private void OnScrubStart(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsScrubbing = true;
    }

    private void OnScrubEnd(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsScrubbing = false;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.LiveCloudUpdated -= OnLiveCloudUpdated;
            _vm.LiveFaulted -= OnLiveFaulted;
        }

        if (DataContext is not MainViewModel vm)
        {
            _vm = null;
            return;
        }

        _vm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.LiveCloudUpdated += OnLiveCloudUpdated;
        vm.LiveFaulted += OnLiveFaulted;
    }

    // Chmura przychodzi z watku odbioru UDP, wiec trzeba ja przeniesc na
    // watek interfejsu - wlasniki widoku nie wolno zmieniac z innego watku.
    private void OnLiveFaulted(string message) =>
        Dispatcher.UIThread.Post(() => _vm?.ApplyLiveFault(message));

    private void OnLiveCloudUpdated(PointCloud cloud) =>
        Dispatcher.UIThread.Post(() => _vm?.ApplyLiveCloud(cloud));

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlaying))
            _timer.IsEnabled = (DataContext as MainViewModel)?.IsPlaying ?? false;
    }

    // Zamkniecie okna nie moze zostawic odbiornika UDP na zywo. Trwajace
    // nagranie trzeba zakonczyc, inaczej manifest nigdy nie powstanie
    // i zapisane klatki nie da sie odtworzyc.
    private async void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _vm?.SimulatorEnabled = false;
        _vm?.StopLiveCommand.Execute(null);

        if (_vm is { IsRecording: true })
            await _vm.StopRecordingAsync();
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

    private void OnResetViewClick(object? sender, RoutedEventArgs e) => ResetView();

    private void ResetView()
    {
        View3D.ResetView();
        View2D.ResetView();
    }

    // Skroty klawiszowe: R - reset widoku, 2 - widok plaszczyzny, 3 - 3D, 4 - kamera z perspektywy lidaru,
    // spacja - odtwarzanie. W polach tekstowych skroty nie dzialaja, bo
    // klawisze musza trafic do wpisywania.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_vm is null || e.Source is TextBox or ComboBox or CheckBox or Slider or Button)
            return;

        switch (e.Key)
        {
            case Key.R:
                ResetView();
                break;
            case Key.D2:
                _vm.ViewMode = ViewMode.Widok2D;
                break;
            case Key.D3:
                _vm.ViewMode = ViewMode.Widok3D;
                break;
            case Key.D4:
                _vm.ViewMode = ViewMode.Kamera;
                break;
            case Key.Space:
                _vm.PlayPauseCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
