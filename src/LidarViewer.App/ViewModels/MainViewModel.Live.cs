using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.App.Streaming;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Readers;
using LidarViewer.Core.Recording;
using LidarViewer.Core.Rendering;
using LidarViewer.Core.Streaming;

namespace LidarViewer.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Pojemnosc bufora punktow w trybie live. Wieksza wartosc daje
    /// dluzsza historie, ale wolniejsze przeladowanie widoku.
    /// </summary>
    public const int LiveBufferCapacity = 200_000;

    private UdpCloudReceiver? _receiver;
    private LiveSimulator? _simulator;
    private bool _isLive;

    [ObservableProperty]
    public partial int LivePort { get; set; } = 2368;

    [ObservableProperty]
    public partial LiveProtocol LiveProtocol { get; set; } = LiveProtocol.Wlasny;

    public LiveProtocol[] LiveProtocols { get; } = Enum.GetValues<LiveProtocol>();

    /// <summary>
    /// Czy odbior UDP pracuje.
    /// </summary>
    public bool IsLive
    {
        get => _isLive;
        private set
        {
            if (_isLive == value)
                return;

            _isLive = value;
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(StatusInfo));
            OnPropertyChanged(nameof(AutoFitCamera));
            OnPropertyChanged(nameof(AutoResetView));
            OnPropertyChanged(nameof(LiveInfo));
            OnPropertyChanged(nameof(CanStartRecording));
        }
    }


    [ObservableProperty]
    public partial bool SimulatorEnabled { get; set; }

    public int LivePointCount => _receiver?.Buffer.Count ?? 0;

    public long LivePacketCount => _receiver?.PacketCount ?? 0;

    public long LiveRejectedCount => _receiver?.RejectedCount ?? 0;

    /// <summary>
    /// Podsumowanie strumienia pokazane przy pasku live.
    /// </summary>
    public string LiveInfo => _receiver is null
        ? string.Empty
        : $"Punkty: {LivePointCount:N0} | Pakiety: {LivePacketCount:N0} | Odrzucone: {LiveRejectedCount:N0}";

    /// <summary>Tempo odbioru do paska stanu: pakiety/s, odswiezenia/s i odrzucone.</summary>
    private string LiveRates => _receiver is null
        ? string.Empty
        : $" | {_receiver.PacketsPerSecond:N0} pkt/s | {_receiver.UpdatesPerSecond:N0} kl./s | odrzucone: {LiveRejectedCount:N0}";

    public IRelayCommand StartLiveCommand { get; }

    public IRelayCommand StopLiveCommand { get; }


    /// <summary>
    /// Nowa chmura z odbiornika. Wywolywane z watku odbioru - widok
    /// przenosi je na watek interfejsu.
    /// </summary>
    public event Action<PointCloud>? LiveCloudUpdated;

    /// <summary>Blad odbioru UDP. Wolane z watku odbioru - okno przenosi je na watek interfejsu.</summary>
    public event Action<string>? LiveFaulted;


    private void StartLiveCore()
    {
        if (_receiver is not null)
            return;

        CloseSequence();

        try
        {
            IPacketDecoder decoder = LiveProtocol switch
            {
                LiveProtocol.VelodyneVlp16 => new VelodyneDecoder(LoadCalibration()),
                _ => new SimpleCloudDecoder(),
            };

            var receiver = new UdpCloudReceiver(LivePort, decoder, LiveBufferCapacity);
            receiver.CloudUpdated += ReceiveLiveCloud;
            receiver.Faulted += ReceiveLiveFault;
            receiver.PacketReceived += RecordRawPacket;
            receiver.Start();

            _receiver = receiver;
            IsLive = true;
            Status = $"Nasłuch UDP na porcie {receiver.Port} ({decoder.Name}).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    private void StopLiveCore()
    {
        if (_receiver is null)
            return;

        _receiver.CloudUpdated -= ReceiveLiveCloud;
        _receiver.Faulted -= ReceiveLiveFault;
        _receiver.PacketReceived -= RecordRawPacket;
        _receiver.Dispose();
        _receiver = null;
        IsLive = false;

        // Po zatrzymaniu odbioru wracamy do chmury z pliku - widok nie
        // powinien zostac z ostatnia stanu na zywo.
        PointCloud = Pipeline.Output;
        Status = "Zatrzymano odbiór na żywo.";
    }

    /// <summary>
    /// Nowa chmura z odbiornika. Wolane z watku odbioru - widok
    /// przenosi ja na watek interfejsu.
    /// </summary>
    public void ReceiveLiveCloud(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        // Klatka nagrywana jest tutaj, a nie w ApplyLiveCloud - strumien
        // dociera do odbioru niezaleznie od tego, czy okno jest otwarte.
        RecordLiveFrame(cloud);
        LiveCloudUpdated?.Invoke(cloud);
    }

    private void ReceiveLiveFault(Exception ex) => LiveFaulted?.Invoke(ex.Message);

    /// <summary>
    /// Konczy tryb live po bledzie odbioru. Wolane z watku interfejsu.
    /// </summary>
    public void ApplyLiveFault(string message)
    {
        StopLiveCore();
        Status = $"Błąd odbioru UDP: {message}";
    }

    /// <summary>
    /// Wstawia chmure otrzymana z odbiornika do widoku. Wolane z watku
    /// interfejsu.
    /// </summary>
    public void ApplyLiveCloud(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        // Filtry dzialaja tez na zywo: chmura przechodzi przez pipeline, a widok dostaje jego wynik.
        Pipeline.Source = cloud;
        PointCloud = Pipeline.Output;
        OnPropertyChanged(nameof(StatusInfo));
        OnPropertyChanged(nameof(LivePointCount));
        OnPropertyChanged(nameof(LivePacketCount));
        OnPropertyChanged(nameof(LiveInfo));
        OnPropertyChanged(nameof(RecordInfo));
    }

    // Klatka nagrywana jest w chwili, gdy chmura trafia na ekran. Recorder
    // odrzuca klatki za szybkie, wiec zapis nie zalewa dysku.
    private void RecordLiveFrame(PointCloud cloud)
    {
        SequenceRecorder? recorder = _recorder;
        if (recorder is null)
            return;

        recorder.AddFrame(cloud);
    }

    partial void OnSimulatorEnabledChanged(bool value)
    {
        if (value)
        {
            // Symulator mowi wlasnym protokolem - inny dekoder odrzucilby
            // wszystkie pakiety.
            LiveProtocol = LiveProtocol.Wlasny;

            if (_receiver is null)
                StartLiveCore();

            if (_receiver is null)
            {
                SimulatorEnabled = false;
                return;
            }

            _simulator?.Dispose();
            _simulator = new LiveSimulator("127.0.0.1", _receiver.Port);
            _simulator.Start();
            Status = "Symulator włączony - pakiety lecą na 127.0.0.1.";
        }
        else
        {
            _simulator?.Dispose();
            _simulator = null;
        }
    }

    partial void OnLiveProtocolChanged(LiveProtocol value)
    {
        // Zmiana formatu pakietow wymaga nowego dekodera, wiec odbior
        // musi wystartowac od nowa.
        if (_receiver is null)
            return;

        _receiver.CloudUpdated -= ReceiveLiveCloud;
        _receiver.Faulted -= ReceiveLiveFault;
        _receiver.PacketReceived -= RecordRawPacket;
        _receiver.Dispose();
        _receiver = null;

        StartLiveCore();
    }
}
