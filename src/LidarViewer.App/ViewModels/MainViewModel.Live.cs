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
    public const int LiveBufferCapacity = 200_000;

    private UdpCloudReceiver? _receiver;
    private LiveSimulator? _simulator;
    private bool _isLive;

    [ObservableProperty]
    public partial int LivePort { get; set; } = 2368;

    [ObservableProperty]
    public partial LiveProtocol LiveProtocol { get; set; } = LiveProtocol.Wlasny;

    public LiveProtocol[] LiveProtocols { get; } = Enum.GetValues<LiveProtocol>();

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

    public string? SimulatorPcapPath { get; set; }

    private bool SimulatorUsesPcap => !string.IsNullOrWhiteSpace(SimulatorPcapPath) && File.Exists(SimulatorPcapPath);

    public int LivePointCount => _receiver?.Buffer.Count ?? 0;

    public long LivePacketCount => _receiver?.PacketCount ?? 0;

    public long LiveRejectedCount => _receiver?.RejectedCount ?? 0;

    public string LiveInfo => _receiver is null
        ? string.Empty
        : $"Punkty: {LivePointCount:N0} | Pakiety: {LivePacketCount:N0} | Odrzucone: {LiveRejectedCount:N0}";

    private string LiveRates => _receiver is null
        ? string.Empty
        : $" | {_receiver.PacketsPerSecond:N0} pkt/s | {_receiver.UpdatesPerSecond:N0} kl./s | odrzucone: {LiveRejectedCount:N0}";

    public IRelayCommand StartLiveCommand { get; }

    public IRelayCommand StopLiveCommand { get; }


    public event Action<PointCloud>? LiveCloudUpdated;

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

            var receiver = new UdpCloudReceiver(LivePort, decoder, LiveBufferCapacity,
                byRevolution: LiveProtocol == LiveProtocol.VelodyneVlp16);
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

        PointCloud = Pipeline.Output;
        Status = "Zatrzymano odbiór na żywo.";
    }

    public void ReceiveLiveCloud(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        RecordLiveFrame(cloud);
        LiveCloudUpdated?.Invoke(cloud);
    }

    private void ReceiveLiveFault(Exception ex) => LiveFaulted?.Invoke(ex.Message);

    public void ApplyLiveFault(string message)
    {
        StopLiveCore();
        Status = $"Błąd odbioru UDP: {message}";
    }

    public void ApplyLiveCloud(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        Pipeline.Source = cloud;
        PointCloud = Pipeline.Output;
        OnPropertyChanged(nameof(StatusInfo));
        OnPropertyChanged(nameof(LivePointCount));
        OnPropertyChanged(nameof(LivePacketCount));
        OnPropertyChanged(nameof(LiveInfo));
        OnPropertyChanged(nameof(RecordInfo));
    }

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
            bool usePcap = SimulatorUsesPcap;
            if (usePcap)
            {
                SensorModel = VelodyneModel.Vlp16;
                CalibrationPath = "";
            }

            LiveProtocol newProtocol = usePcap ? LiveProtocol.VelodyneVlp16 : LiveProtocol.Wlasny;
            if (LiveProtocol != newProtocol && _receiver is not null)
                StopLiveCore();

            LiveProtocol = newProtocol;

            if (_receiver is null)
                StartLiveCore();

            if (_receiver is null)
            {
                SimulatorEnabled = false;
                return;
            }

            _simulator?.Dispose();
            _simulator = new LiveSimulator("127.0.0.1", _receiver.Port, usePcap ? SimulatorPcapPath : null);
            _simulator.Start();
            Status = usePcap
                ? $"Symulator włączony - odtwarzanie {Path.GetFileName(SimulatorPcapPath)} na 127.0.0.1."
                : "Symulator włączony - pakiety lecą na 127.0.0.1.";
        }
        else
        {
            _simulator?.Dispose();
            _simulator = null;
        }
    }

    partial void OnSensorModelChanged(VelodyneModel value) => RestartLiveIfVelodyne();

    partial void OnCalibrationPathChanged(string value) => RestartLiveIfVelodyne();

    private void RestartLiveIfVelodyne()
    {
        if (LiveProtocol == LiveProtocol.VelodyneVlp16)
            OnLiveProtocolChanged(LiveProtocol);
    }

    partial void OnLiveProtocolChanged(LiveProtocol value)
    {
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
