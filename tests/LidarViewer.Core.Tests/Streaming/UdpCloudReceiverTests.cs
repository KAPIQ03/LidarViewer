using System.Net;
using System.Net.Sockets;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class UdpCloudReceiverTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static byte[] Packet(int points = 3) =>
        SimpleCloudProtocol.Encode(new float[points * 3]);

    [Fact]
    public async Task ReceiverReportsBoundPort()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 100);

        Assert.True(receiver.Port > 0);
    }

    [Fact]
    public async Task ReceivedPacketProducesCloudEvent()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 100);
        receiver.Start();

        PointCloudEvent? received = await WaitForCloud(receiver);

        Assert.NotNull(received);
        Assert.Equal(3, received!.Cloud.Count);
    }

    [Fact]
    public async Task RejectedPacketDoesNotProduceCloudEvent()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 100);
        receiver.Start();

        using var sender = new UdpClient();
        await sender.SendAsync(new byte[] { 1, 2, 3 }, new IPEndPoint(IPAddress.Loopback, receiver.Port));

        await Task.Delay(300);

        Assert.Equal(1, receiver.RejectedCount);
        Assert.Equal(0, receiver.PacketCount);
    }

    [Fact]
    public async Task ReceivedPacketsAccumulateInBuffer()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 100);
        receiver.Start();

        using var sender = new UdpClient();
        await sender.SendAsync(Packet(3), new IPEndPoint(IPAddress.Loopback, receiver.Port));
        await Task.Delay(200);
        await sender.SendAsync(Packet(4), new IPEndPoint(IPAddress.Loopback, receiver.Port));

        await Task.Delay(400);

        Assert.Equal(2, receiver.PacketCount);
        Assert.True(receiver.Buffer.Count >= 3);
    }

    [Fact]
    public async Task StopEndsReceiving()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 100);
        receiver.Start();
        receiver.Stop();

        using var sender = new UdpClient();
        await sender.SendAsync(Packet(3), new IPEndPoint(IPAddress.Loopback, receiver.Port));

        await Task.Delay(300);

        Assert.Equal(0, receiver.PacketCount);
    }

    [Fact]
    public async Task LastPacketsAreFlushedAfterThrottleWindow()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 100);
        var allPoints = new TaskCompletionSource<bool>();
        receiver.CloudUpdated += cloud =>
        {
            if (cloud.Count == 7)
                allPoints.TrySetResult(true);
        };
        receiver.Start();

        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Loopback, receiver.Port);
        await sender.SendAsync(Packet(3), endpoint);
        await Task.Delay(20);
        await sender.SendAsync(Packet(4), endpoint);

        Task completed = await Task.WhenAny(allPoints.Task, Task.Delay(Timeout));

        Assert.Same(allPoints.Task, completed);
    }

    private static async Task<PointCloudEvent?> WaitForCloud(UdpCloudReceiver receiver)
    {
        var completion = new TaskCompletionSource<PointCloudEvent>();

        void Handler(Core.Models.PointCloud cloud) =>
            completion.TrySetResult(new PointCloudEvent(cloud));

        receiver.CloudUpdated += Handler;
        try
        {
            using var sender = new UdpClient();
            await sender.SendAsync(Packet(3), new IPEndPoint(IPAddress.Loopback, receiver.Port));

            Task completed = await Task.WhenAny(completion.Task, Task.Delay(Timeout));
            return completed == completion.Task ? await completion.Task : null;
        }
        finally
        {
            receiver.CloudUpdated -= Handler;
        }
    }

    private sealed record PointCloudEvent(Core.Models.PointCloud Cloud);
}
