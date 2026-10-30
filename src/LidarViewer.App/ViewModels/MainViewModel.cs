using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net.Sockets;
using LidarViewer.Core.IO;
using LidarViewer.Core.Models;
using LidarViewer.Core.Networking;
using LidarViewer.Core.Recording;
using System.Xml;

namespace LidarViewer.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private const int MaximumLivePoints = 100_000;
    private readonly UdpLidarReceiver _receiver = new();
    private CsvPointCloudRecorder? _recorder;
    private bool _liveMode;

    public MainViewModel()
    {
        _receiver.FrameReceived += OnFrameReceivedAsync;
        _receiver.PacketRejected += message =>
            Dispatcher.UIThread.Post(() => StatusText = $"Odrzucono pakiet UDP: {message}");
        _receiver.ReceiveError += exception =>
            Dispatcher.UIThread.Post(() => _ = HandleReceiveErrorAsync(exception));
    }

    [ObservableProperty]
    private IReadOnlyList<LidarPoint> _points = Array.Empty<LidarPoint>();

    [ObservableProperty]
    private string _udpPort = "2368";

    [ObservableProperty]
    private string _statusText = "Załaduj plik .vtp lub uruchom odbiór UDP.";

    [ObservableProperty]
    private string _pointCountText = "0";

    [ObservableProperty]
    private string _rangeText = "—";

    [ObservableProperty]
    private string _averageIntensityText = "—";

    [ObservableProperty]
    private string _modeText = "BRAK ŹRÓDŁA";

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string _recordingPath = string.Empty;

    [ObservableProperty]
    private string _fileName = "Nie wybrano pliku";

    partial void OnPointsChanged(IReadOnlyList<LidarPoint> value)
    {
        var statistics = PointCloudStatistics.Calculate(value);
        PointCountText = statistics.PointCount.ToString("N0");
        RangeText = statistics.PointCount == 0
            ? "—"
            : $"X {statistics.MinX:F2}…{statistics.MaxX:F2}   " +
              $"Y {statistics.MinY:F2}…{statistics.MaxY:F2}   " +
              $"Z {statistics.MinZ:F2}…{statistics.MaxZ:F2}";
        AverageIntensityText = statistics.PointCount == 0
            ? "—"
            : statistics.AverageIntensity.ToString("F2");
    }

    [RelayCommand]
    private async Task StartListeningAsync()
    {
        if (IsListening)
        {
            StatusText = "Odbiór UDP jest już uruchomiony.";
            return;
        }

        if (!int.TryParse(UdpPort, out var port) || port is < 1 or > 65535)
        {
            StatusText = "Podaj prawidłowy port UDP z zakresu 1–65535.";
            return;
        }

        try
        {
            await _receiver.StartAsync(port);
            _liveMode = true;
            IsListening = true;
            ModeText = "TRANSMISJA NA ŻYWO";
            FileName = "Wejście UDP";
            Points = Array.Empty<LidarPoint>();
            StatusText = $"Nasłuchiwanie UDP na porcie {port}. Oczekiwanie na pakiety JSON…";
        }
        catch (SocketException exception)
        {
            StatusText = $"Nie można uruchomić odbioru UDP: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task StopListeningAsync()
    {
        await _receiver.StopAsync();
        if (_recorder is not null)
        {
            await StopRecordingAsync();
        }

        IsListening = false;
        if (_liveMode)
        {
            _liveMode = false;
            ModeText = IsRecording ? "ZAPIS NAGRANIA" : "ZATRZYMANO";
        }

        StatusText = "Odbiór UDP zatrzymany.";
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (_recorder is null)
        {
            return;
        }

        await _recorder.DisposeAsync();
        _recorder = null;
        IsRecording = false;
        ModeText = IsListening ? "TRANSMISJA NA ŻYWO" : "GOTOWY";
        StatusText = $"Nagranie zapisano: {RecordingPath}";
    }

    public async Task LoadFileAsync(string path)
    {
        try
        {
            var extension = Path.GetExtension(path);
            IReadOnlyList<LidarPoint> loadedPoints = extension.Equals(".vtp", StringComparison.OrdinalIgnoreCase)
                ? await VtpReader.ReadAsync(path)
                : extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    ? await CsvPointCloudReader.ReadAsync(path)
                    : throw new NotSupportedException("Obsługiwane formaty wejściowe to .vtp i .csv.");

            await StopListeningIfNeededAsync();
            Points = loadedPoints;
            _liveMode = false;
            FileName = Path.GetFileName(path);
            ModeText = extension.Equals(".vtp", StringComparison.OrdinalIgnoreCase) ? "ANALIZA PLIKU VTP" : "ODTWARZANIE CSV";
            StatusText = $"Wczytano {loadedPoints.Count:N0} punktów z pliku {FileName}.";
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or NotSupportedException or
                UnauthorizedAccessException or XmlException)
        {
            StatusText = $"Nie udało się wczytać pliku: {exception.Message}";
        }
    }

    public async Task StartRecordingAsync(string path)
    {
        if (IsRecording)
        {
            StatusText = "Nagrywanie jest już aktywne.";
            return;
        }

        if (!IsListening)
        {
            StatusText = "Najpierw uruchom odbiór UDP, aby rozpocząć nagrywanie.";
            return;
        }

        try
        {
            _recorder = await CsvPointCloudRecorder.CreateAsync(path);
            RecordingPath = path;
            IsRecording = true;
            ModeText = "NAGRYWANIE";
            StatusText = $"Nagrywanie punktów do {path}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Nie udało się utworzyć nagrania: {exception.Message}";
        }
    }

    private async Task OnFrameReceivedAsync(IReadOnlyList<LidarPoint> frame, DateTimeOffset timestamp)
    {
        if (_recorder is not null)
        {
            try
            {
                await _recorder.AppendAsync(frame, timestamp);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await _recorder.DisposeAsync();
                _recorder = null;
                Dispatcher.UIThread.Post(() =>
                {
                    IsRecording = false;
                    StatusText = $"Zapis nagrania przerwany: {exception.Message}";
                });
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_liveMode)
            {
                return;
            }

            var merged = Points.Concat(frame).TakeLast(MaximumLivePoints).ToArray();
            Points = merged;
            if (IsListening)
            {
                StatusText = $"Odebrano {frame.Count:N0} punktów · {DateTime.Now:HH:mm:ss}";
            }
        });
    }

    private async Task HandleReceiveErrorAsync(Exception exception)
    {
        IsListening = false;
        _liveMode = false;
        if (_recorder is not null)
        {
            try
            {
                await StopRecordingAsync();
            }
            catch (Exception recordingException) when (
                recordingException is IOException or UnauthorizedAccessException)
            {
                StatusText = $"Nie udało się zamknąć pliku nagrania: {recordingException.Message}";
            }
        }

        ModeText = "BŁĄD UDP";
        StatusText = $"Błąd odbioru UDP: {exception.Message}";
    }

    private async Task StopListeningIfNeededAsync()
    {
        if (IsListening || _receiver.IsListening)
        {
            await StopListeningAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _receiver.DisposeAsync();
        if (_recorder is not null)
        {
            await _recorder.DisposeAsync();
            _recorder = null;
        }
    }
}
