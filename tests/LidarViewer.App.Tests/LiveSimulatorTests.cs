using LidarViewer.App.Streaming;
using LidarViewer.Core.Streaming;

namespace LidarViewer.App.Tests;

public class LiveSimulatorTests
{
    [Fact]
    public void BuildSweepProducesFullScan()
    {
        byte[][] packets = LiveSimulator.BuildSweep(0f);

        Assert.True(packets.Length > 1);

        var decoder = new SimpleCloudDecoder();
        int points = 0;
        foreach (byte[] packet in packets)
        {
            Assert.True(decoder.TryDecode(packet, out var cloud));
            points += cloud!.Count;
        }

        Assert.Equal(360 * 16, points);
    }

    [Fact]
    public void BuildSweepMovesWithPhase()
    {
        byte[][] first = LiveSimulator.BuildSweep(0f);
        byte[][] second = LiveSimulator.BuildSweep(90f);

        Assert.NotEqual(first[0][10], second[0][10]);
    }

    [Fact]
    public async Task SimulatorFeedsReceiver()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 20000);
        receiver.Start();

        using var simulator = new LiveSimulator("127.0.0.1", receiver.Port);
        simulator.Start();

        await Task.Delay(500);

        Assert.True(receiver.PacketCount > 0, "Symulator nie wyslal zadnego pakietu.");
        Assert.True(receiver.Buffer.Count > 0, "Odbiornik nie zakumulowal punktow.");
    }

    [Fact]
    public async Task SimulatorReplaysPcapInLoop()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sim-{Guid.NewGuid():N}.pcap");
        try
        {
            using (var writer = new PcapWriter(path))
            {
                var start = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
                for (int i = 0; i < 5; i++)
                {
                    writer.Write(new byte[1206], start.AddMilliseconds(i * 10));
                    writer.Write(new byte[512], start.AddMilliseconds(i * 10 + 5));
                }
            }

            using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 20000);
            receiver.Start();

            using var simulator = new LiveSimulator("127.0.0.1", receiver.Port, path);
            simulator.Start();

            await Task.Delay(500);
            long received = receiver.PacketCount + receiver.RejectedCount;
            Assert.True(received > 5, "Symulator nie zapetlil nagrania.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SimulatorRejectsInvalidPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSimulator("127.0.0.1", 0));
    }
}
