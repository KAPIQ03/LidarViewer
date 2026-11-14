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
    public void SimulatorRejectsInvalidPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSimulator("127.0.0.1", 0));
    }
}
