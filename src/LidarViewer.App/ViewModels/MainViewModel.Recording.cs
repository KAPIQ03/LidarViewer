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
using LidarViewer.Core.Rendering;
using LidarViewer.Core.Streaming;

namespace LidarViewer.App.ViewModels;

public partial class MainViewModel
{
    private PcapWriter? _pcapWriter;
    private string? _pcapPath;
    private volatile string? _pcapError;

    public bool IsRecording
    {
        get => _pcapWriter is not null;
    }

    public bool RecordsRawPackets => IsLive && LiveProtocol == LiveProtocol.VelodyneVlp16;

    public long RecordedPacketCount => _pcapWriter?.PacketCount ?? 0;

    public bool CanStartRecording => RecordsRawPackets && !IsRecording;

    public string RecordInfo =>
        _pcapWriter is not null ? $"Nagrane pakiety: {RecordedPacketCount:N0}" : string.Empty;

    public void ExportCloud(string path)
    {
        if (PointCloud is null)
        {
            Status = "Błąd: nie ma chmury do zapisania.";
            return;
        }

        try
        {
            PointCloudWriter.Write(PointCloud, path);
            Status = $"Zapisano {PointCloud.Count:N0} punktów do {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    public void StartPcapRecording(string path)
    {
        if (IsRecording)
            return;

        if (!IsLive)
        {
            Status = "Błąd: nagrywanie pcap wymaga włączonego odbioru UDP.";
            return;
        }

        try
        {
            _pcapError = null;
            _pcapPath = path;
            _pcapWriter = new PcapWriter(path, destinationPort: (ushort)(_receiver?.Port ?? LivePort));
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanStartRecording));
            OnPropertyChanged(nameof(RecordInfo));
            Status = $"Nagrywanie pakietów do {path}.";
        }
        catch (Exception ex)
        {
            _pcapWriter = null;
            Status = $"Błąd: {ex.Message}";
        }
    }

    private void RecordRawPacket(ReadOnlyMemory<byte> payload, DateTimeOffset time)
    {
        PcapWriter? writer = _pcapWriter;
        if (writer is null)
            return;

        try
        {
            writer.Write(payload.Span, time);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _pcapError = ex.Message;
        }
    }

    public async Task StopRecordingAsync()
    {
        if (_pcapWriter is { } pcapWriter)
        {
            _pcapWriter = null;
            string? error = _pcapError;
            long packets = pcapWriter.PacketCount;
            pcapWriter.Dispose();
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanStartRecording));
            OnPropertyChanged(nameof(RecordInfo));
            Status = error is null
                ? $"Zapisano nagranie pcap: {_pcapPath} ({packets:N0} pakietów)."
                : $"Błąd zapisu pcap: {error}";
            return;
        }
    }
}
