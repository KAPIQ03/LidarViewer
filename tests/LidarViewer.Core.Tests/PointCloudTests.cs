using LidarViewer.Core.IO;
using LidarViewer.Core.Models;
using LidarViewer.Core.Networking;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LidarViewer.Core.Tests;

public sealed class PointCloudTests
{
    [Fact]
    public void CalculateStatisticsReturnsBoundsAndMeanIntensity()
    {
        var points = new[]
        {
            new LidarPoint(-1, 2, 0, 0.2),
            new LidarPoint(3, -2, 4, 0.8)
        };

        var result = PointCloudStatistics.Calculate(points);

        Assert.Equal(2, result.PointCount);
        Assert.Equal(-1, result.MinX);
        Assert.Equal(3, result.MaxX);
        Assert.Equal(-2, result.MinY);
        Assert.Equal(2, result.MaxY);
        Assert.Equal(0, result.MinZ);
        Assert.Equal(4, result.MaxZ);
        Assert.Equal(0.5, result.AverageIntensity);
    }

    [Fact]
    public async Task ReadVtpReadsAsciiPointsAndIntensity()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                <?xml version="1.0"?>
                <VTKFile type="PolyData" version="1.0" byte_order="LittleEndian">
                  <PolyData>
                    <Piece NumberOfPoints="2">
                      <PointData Scalars="Intensity">
                        <DataArray type="Float32" Name="Intensity" format="ascii">0.25 0.75</DataArray>
                      </PointData>
                      <Points>
                        <DataArray type="Float32" NumberOfComponents="3" format="ascii">1 2 3 4 5 6</DataArray>
                      </Points>
                    </Piece>
                  </PolyData>
                </VTKFile>
                """);

            var points = await VtpReader.ReadAsync(path);

            Assert.Collection(points,
                point =>
                {
                    Assert.Equal(new LidarPoint(1, 2, 3, 0.25), point);
                },
                point =>
                {
                    Assert.Equal(new LidarPoint(4, 5, 6, 0.75), point);
                });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadVtpRejectsBinaryDataArraysWithClearMessage()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                <VTKFile type="PolyData">
                  <PolyData><Piece>
                    <Points><DataArray type="Float32" NumberOfComponents="3" format="binary">AAAA</DataArray></Points>
                  </Piece></PolyData>
                </VTKFile>
                """);

            var exception = await Assert.ThrowsAsync<NotSupportedException>(() => VtpReader.ReadAsync(path));
            Assert.Contains("ASCII", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CsvRecorderOutputCanBeReadBack()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using (var recorder = await LidarViewer.Core.Recording.CsvPointCloudRecorder.CreateAsync(path))
            {
                await recorder.AppendAsync(
                    new[] { new LidarPoint(1.25, -2, 3.5, 0.8) },
                    new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero));
            }

            var points = await CsvPointCloudReader.ReadAsync(path);

            Assert.Equal(new LidarPoint(1.25, -2, 3.5, 0.8), Assert.Single(points));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UdpReceiverPublishesJsonPointDatagrams()
    {
        int port;
        using (var portProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            port = ((IPEndPoint)portProbe.Client.LocalEndPoint!).Port;
        }

        await using var receiver = new UdpLidarReceiver();
        var received = new TaskCompletionSource<IReadOnlyList<LidarPoint>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.FrameReceived += (points, _) =>
        {
            received.TrySetResult(points);
            return Task.CompletedTask;
        };
        await receiver.StartAsync(port);

        using var sender = new UdpClient();
        var payload = Encoding.UTF8.GetBytes("""{"points":[[1,2,3,0.5]]}""");
        await sender.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, port));
        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new LidarPoint(1, 2, 3, 0.5), Assert.Single(frame));
    }
}
