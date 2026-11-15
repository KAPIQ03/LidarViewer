using System.Net;
using System.Net.Sockets;
using LidarViewer.App.ViewModels;
using LidarViewer.Core.Streaming;

namespace LidarViewer.App.Tests;

public class PcapRecordingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lvpcap-" + Guid.NewGuid().ToString("N"));

    public PcapRecordingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task LiveVelodyneStreamIsRecordedAsPlayablePcap()
    {
        var vm = new MainViewModel { LivePort = 34590, LiveProtocol = LiveProtocol.VelodyneVlp16 };
        vm.StartLiveCommand.Execute(null);
        Assert.True(vm.RecordsRawPackets);

        string path = Path.Combine(_dir, "live.pcap");
        vm.StartPcapRecording(path);
        Assert.True(vm.IsRecording);

        using var sender = new UdpClient();
        var target = new IPEndPoint(IPAddress.Loopback, 34590);
        await sender.SendAsync(new byte[1206], target);
        await sender.SendAsync(new byte[] { 1, 2, 3 }, target);

        for (int i = 0; i < 50 && vm.RecordedPacketCount < 2; i++)
            await Task.Delay(100);

        Assert.Equal("Nagrane pakiety: 2", vm.RecordInfo);

        await vm.StopRecordingAsync();
        string status = vm.Status;
        vm.StopLiveCommand.Execute(null);

        Assert.False(vm.IsRecording);
        Assert.Contains("2 pakiet", status);
        var packets = PcapReader.ReadUdpPackets(path).ToList();
        Assert.Equal(2, packets.Count);
        Assert.Equal(1206, packets[0].Payload.Length);
    }

    [Fact]
    public void PcapRecordingNeedsRunningReceiver()
    {
        var vm = new MainViewModel();

        vm.StartPcapRecording(Path.Combine(_dir, "x.pcap"));

        Assert.False(vm.IsRecording);
        Assert.StartsWith("Błąd", vm.Status);
    }

    [Fact]
    public void CustomProtocolKeepsFrameRecording()
    {
        var vm = new MainViewModel { LivePort = 34591, LiveProtocol = LiveProtocol.Wlasny };
        vm.StartLiveCommand.Execute(null);

        Assert.False(vm.RecordsRawPackets);
        vm.StopLiveCommand.Execute(null);
    }
}
