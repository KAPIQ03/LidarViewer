using System.Net;
using System.Net.Sockets;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Odbiorca chmur punktow po UDP.
///
/// Nasluchuje na wskazanym porcie, dekoduje pakiety i kumuluje je w buforze.
/// Zdarzenie CloudUpdated jest wywolywane najwyzej 10 razy na sekunde, zeby
/// interfejs nadazal do przychodzacego strumienia.
/// </summary>
public sealed class UdpCloudReceiver : IDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(100);

    private readonly IPacketDecoder _decoder;
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cts = new();
    private long _packetCount;
    private long _rejectedCount;
    private DateTime _nextUpdate = DateTime.MinValue;

    public UdpCloudReceiver(int port, IPacketDecoder decoder, int capacity)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _decoder = decoder;
        Buffer = new LiveCloudBuffer(capacity);
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, port));

        // Domyslny bufor odbioru jest za maly na wieksze datagramy - pakiet
        // zostalby urwany albo odrzucony.
        _client.Client.ReceiveBufferSize = 1 << 20;
        Port = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Faktyczny port, na ktorym nasluchuje. Przy porcie 0 jest to port
    /// przydzielony przez system.
    /// </summary>
    public int Port { get; }

    public LiveCloudBuffer Buffer { get; }

    public long PacketCount => Interlocked.Read(ref _packetCount);

    public long RejectedCount => Interlocked.Read(ref _rejectedCount);

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Nowa chmura z bufora. Wywolywane z watku odbioru.
    /// </summary>
    public event Action<PointCloud>? CloudUpdated;

    public void Start()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _cts.Cancel();
        _client.Close();
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _client.ReceiveAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            if (!_decoder.TryDecode(result.Buffer, out PointCloud? cloud) || cloud is null)
            {
                Interlocked.Increment(ref _rejectedCount);
                continue;
            }

            Interlocked.Increment(ref _packetCount);
            Buffer.Add(cloud);
            Publish();
        }
    }

    private void Publish()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;
        CloudUpdated?.Invoke(Buffer.Snapshot());
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
        _client.Dispose();
    }
}
