using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Networking;

public sealed class UdpLidarReceiver : IAsyncDisposable
{
    private UdpClient? _client;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveTask;

    public bool IsListening => _client is not null && _receiveTask is { IsCompleted: false };

    public event Func<IReadOnlyList<LidarPoint>, DateTimeOffset, Task>? FrameReceived;
    public event Action<string>? PacketRejected;
    public event Action<Exception>? ReceiveError;

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The UDP port must be between 1 and 65535.");
        }

        if (_client is not null && _receiveTask is { IsCompleted: false })
        {
            throw new InvalidOperationException("The UDP receiver is already listening.");
        }

        _client?.Dispose();
        _cancellation?.Dispose();
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = ReceiveLoopAsync(_client, _cancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_client is null)
        {
            return;
        }

        _cancellation?.Cancel();
        _client.Dispose();

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _client = null;
        _receiveTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try
            {
                packet = await client.ReceiveAsync(cancellationToken);
            }
            catch (SocketException exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                ReceiveError?.Invoke(exception);
                return;
            }

            IReadOnlyList<LidarPoint> points;
            try
            {
                points = ParsePacket(packet.Buffer);
            }
            catch (InvalidDataException exception)
            {
                PacketRejected?.Invoke(exception.Message);
                continue;
            }

            var handler = FrameReceived;
            if (handler is not null && points.Count > 0)
            {
                try
                {
                    await handler(points, DateTimeOffset.UtcNow);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ReceiveError?.Invoke(exception);
                    return;
                }
            }
        }
    }

    private static IReadOnlyList<LidarPoint> ParsePacket(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            var root = document.RootElement;
            JsonElement pointArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                pointArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("points", out var pointsElement))
            {
                pointArray = pointsElement;
            }
            else
            {
                throw new InvalidDataException("UDP JSON must be an array or an object containing a 'points' array.");
            }

            if (pointArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The UDP 'points' value must be an array.");
            }

            var points = new List<LidarPoint>(pointArray.GetArrayLength());
            foreach (var element in pointArray.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    if (element.GetArrayLength() < 3)
                    {
                        throw new InvalidDataException("Each UDP point array must contain at least X, Y, and Z.");
                    }

                    points.Add(new LidarPoint(
                        ReadNumber(element[0], "x"),
                        ReadNumber(element[1], "y"),
                        ReadNumber(element[2], "z"),
                        element.GetArrayLength() > 3 ? ReadNumber(element[3], "intensity") : 0));
                }
                else if (element.ValueKind == JsonValueKind.Object &&
                         element.TryGetProperty("x", out var x) &&
                         element.TryGetProperty("y", out var y) &&
                         element.TryGetProperty("z", out var z))
                {
                    points.Add(new LidarPoint(
                        ReadNumber(x, "x"),
                        ReadNumber(y, "y"),
                        ReadNumber(z, "z"),
                        element.TryGetProperty("intensity", out var intensity)
                            ? ReadNumber(intensity, "intensity")
                            : 0));
                }
                else
                {
                    throw new InvalidDataException("Each UDP point must be an XYZ array or an object with x, y, and z.");
                }
            }

            return points;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The UDP datagram is not valid JSON.", exception);
        }
    }

    private static double ReadNumber(JsonElement value, string coordinateName)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number) ||
            !double.IsFinite(number))
        {
            throw new InvalidDataException($"UDP point value '{coordinateName}' must be a finite number.");
        }

        return number;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
