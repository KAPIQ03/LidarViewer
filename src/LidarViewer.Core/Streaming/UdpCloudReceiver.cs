using System.Net;
using System.Net.Sockets;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

public sealed class UdpCloudReceiver : IDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(100);

    private readonly IPacketDecoder _decoder;
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cts = new();
    private long _packetCount;
    private long _rejectedCount;
    private readonly RateMeter _packetRate = new();
    private readonly RateMeter _frameRate = new();
    private readonly object _gate = new();
    private readonly Timer _flushTimer;
    private DateTime _nextUpdate = DateTime.MinValue;
    private bool _dirty;
    private bool _flushScheduled;

    public UdpCloudReceiver(int port, IPacketDecoder decoder, int capacity, bool byRevolution = false)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _decoder = decoder;
        Buffer = new LiveCloudBuffer(capacity, byRevolution);
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, port));

        _client.Client.ReceiveBufferSize = 1 << 20;
        Port = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
        _flushTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public int Port { get; }

    public LiveCloudBuffer Buffer { get; }

    public long PacketCount => Interlocked.Read(ref _packetCount);

    public long RejectedCount => Interlocked.Read(ref _rejectedCount);

    public double PacketsPerSecond => _packetRate.PerSecond();

    public double UpdatesPerSecond => _frameRate.PerSecond();

    public bool IsRunning { get; private set; }

    public event Action<PointCloud>? CloudUpdated;

    public event Action<Exception>? Faulted;

    public event Action<ReadOnlyMemory<byte>, DateTimeOffset>? PacketReceived;

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
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue;
            }
            catch (SocketException ex)
            {
                if (token.IsCancellationRequested)
                    return;

                IsRunning = false;
                Faulted?.Invoke(ex);
                return;
            }

            _packetRate.Add();
            PacketReceived?.Invoke(result.Buffer, DateTimeOffset.UtcNow);

            if (!_decoder.TryDecode(result.Buffer, out PointCloud? cloud) || cloud is null)
            {
                Interlocked.Increment(ref _rejectedCount);
                continue;
            }

            Interlocked.Increment(ref _packetCount);
            lock (_gate)
                Buffer.Add(cloud);
            Publish();
        }
    }

    private void Publish()
    {
        PointCloud snapshot;
        lock (_gate)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextUpdate)
            {
                _dirty = true;
                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    _flushTimer.Change(_nextUpdate - now, Timeout.InfiniteTimeSpan);
                }
                return;
            }

            _nextUpdate = now + UpdateInterval;
            _dirty = false;
            snapshot = Buffer.Snapshot();
        }

        _frameRate.Add();
        CloudUpdated?.Invoke(snapshot);
    }

    private void Flush()
    {
        PointCloud snapshot;
        lock (_gate)
        {
            _flushScheduled = false;
            if (!_dirty || !IsRunning)
                return;

            _nextUpdate = DateTime.UtcNow + UpdateInterval;
            _dirty = false;
            snapshot = Buffer.Snapshot();
        }

        _frameRate.Add();
        CloudUpdated?.Invoke(snapshot);
    }

    public void Dispose()
    {
        Stop();
        _flushTimer.Dispose();
        _cts.Dispose();
        _client.Dispose();
    }
}
