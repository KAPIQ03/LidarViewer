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

    private async void OnBrowseCalibrationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz plik kalibracji Velodyne (db.xml)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Kalibracja Velodyne") { Patterns = ["*.xml"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.CalibrationPath = path;
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
                new FilePickerFileType("Chmura PCD") { Patterns = ["*.pcd"] },
                new FilePickerFileType("Chmura LAS") { Patterns = ["*.las"] },
                new FilePickerFileType("Punkty tekstowe") { Patterns = ["*.xyz", "*.csv"] },
            ],
        });

        if (file?.TryGetLocalPath() is { } path)
            vm.ExportCloud(path);
    }

    private async void OnScreenshotClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz zrzut widoku",
            SuggestedFileName = $"widok-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("Obraz PNG") { Patterns = ["*.png"] }],
        });

        if (file?.TryGetLocalPath() is not { } path)
            return;

        try
        {
            if (vm.Is3D)
            {
                // Widok OpenGL nie trafia do RenderTargetBitmap - piksele czyta sam z bufora ramki.
                View3D.RequestScreenshot((width, height, rgba) => SaveRgbaAsPng(vm, path, width, height, rgba));
            }
            else
            {
                var size = new Avalonia.PixelSize(Math.Max(1, (int)View2D.Bounds.Width), Math.Max(1, (int)View2D.Bounds.Height));
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                bitmap.Render(View2D);
                bitmap.Save(path);
                vm.Status = $"Zapisano zrzut widoku: {System.IO.Path.GetFileName(path)}.";
            }
        }
        catch (Exception ex)
        {
            vm.Status = $"Błąd: {ex.Message}";
        }
    }

    private static void SaveRgbaAsPng(MainViewModel vm, string path, int width, int height, byte[] rgba)
    {
        try
        {
            using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
            {
                for (int row = 0; row < height; row++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        rgba, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
            }

            bitmap.Save(path);
            vm.Status = $"Zapisano zrzut widoku: {System.IO.Path.GetFileName(path)} ({width}×{height}).";
        }
        catch (Exception ex)
        {
            vm.Status = $"Błąd: {ex.Message}";
        }
    }

    private async void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Protokol Velodyne zapisuje surowe pakiety do jednego pliku .pcap (jak VeloView).
        if (vm.RecordsRawPackets)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Zapisz nagranie pcap",
                SuggestedFileName = $"nagranie-{DateTime.Now:yyyyMMdd-HHmmss}.pcap",
                DefaultExtension = "pcap",
                FileTypeChoices = [new FilePickerFileType("Nagranie pcap") { Patterns = ["*.pcap"] }],
            });

            if (file?.TryGetLocalPath() is { } pcapPath)
                vm.StartPcapRecording(pcapPath);
            return;
        }

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
    // spacja - odtwarzanie, strzalki - krok klatki, Home/End - pierwsza/ostatnia klatka. W polach tekstowych skroty nie dzialaja, bo
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
            case Key.Left:
                _vm.StepBackCommand.Execute(null);
                break;
            case Key.Right:
                _vm.StepForwardCommand.Execute(null);
                break;
            case Key.Home:
                _vm.FirstFrameCommand.Execute(null);
                break;
            case Key.End:
                _vm.LastFrameCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
