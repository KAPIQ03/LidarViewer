using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LidarViewer.Core.Streaming;

namespace LidarViewer.App.Streaming;

/// <summary>
/// Symulator czujnika lidar - generuje chmure punktow i wysyla je po UDP
/// wlasnym protokolem. Pozwala sprawdzic tryb live bez sprzetu.
///
/// Generowany obiekt to wirujacy wiatrak 360 kolumn x 16 poziomow, ktory
/// zmienia ksztalt w czasie - tak jak chmura z prawdziwego czujnika.
/// </summary>
public sealed class LiveSimulator : IDisposable
{
    private const int Columns = 360;
    private const int Elevations = 16;
    private const int PointsPerPacket = 1000;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private static readonly float[] VerticalAngles =
    [
        -15f, 1f, -13f, 3f, -11f, 5f, -9f, 7f,
        -7f, 9f, -5f, 11f, -3f, 13f, -1f, 15f,
    ];

    private readonly UdpClient _client = new();
    private readonly IPEndPoint _target;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public LiveSimulator(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        // Domyslny bufor wysylki w systemie jest za maly na wieksze pakiety
        // UDP - bez tego wyslanie konczy sie bledem "Message too long".
        _client.Client.SendBufferSize = 1 << 20;
        _target = new IPEndPoint(Resolve(host), port);
    }

    public bool IsRunning => _loop is not null;

    public void Start()
    {
        if (_loop is not null)
            return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _loop = Task.Run(() => RunAsync(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        float phase = 0;

        while (!token.IsCancellationRequested)
        {
            foreach (byte[] packet in BuildSweep(phase))
            {
                try
                {
                    await _client.SendAsync(packet, _target).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    return;
                }
            }

            phase += 360f * (float)Interval.TotalSeconds / 4f;

            try
            {
                await Task.Delay(Interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Buduje pakiety jednego obrotu wiatraka dla podanego fazy.
    /// </summary>
    public static byte[][] BuildSweep(float phase)
    {
        var positions = new float[Columns * Elevations * 3];
        var intensity = new byte[Columns * Elevations];

        int index = 0;
        for (int column = 0; column < Columns; column++)
        {
            float azimuth = (column + phase) * MathF.PI / 180f;
            for (int level = 0; level < Elevations; level++)
            {
                float vertical = VerticalAngles[level] * MathF.PI / 180f;
                float distance = 10f
                    + 2f * MathF.Sin((column + phase) * 0.05f)
                    + 1.5f * MathF.Cos(level * 0.4f);

                positions[index * 3] = distance * MathF.Cos(azimuth) * MathF.Cos(vertical);
                positions[index * 3 + 1] = distance * MathF.Sin(azimuth) * MathF.Cos(vertical);
                positions[index * 3 + 2] = distance * MathF.Sin(vertical);
                intensity[index] = (byte)Math.Clamp(255f * (1f - distance / 15f), 0f, 255f);
                index++;
            }
        }

        int total = Columns * Elevations;
        var packets = new List<byte[]>();
        for (int offset = 0; offset < total; offset += PointsPerPacket)
        {
            int take = Math.Min(PointsPerPacket, total - offset);
            packets.Add(SimpleCloudProtocol.Encode(
                positions.AsSpan(offset * 3, take * 3),
                intensity.AsSpan(offset, take).ToArray()));
        }

        return packets.ToArray();
    }

    private static IPAddress Resolve(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress? address))
            return address;

        IPAddress[] addresses = Dns.GetHostAddresses(host);
        if (addresses.Length == 0)
            throw new ArgumentException($"Nie udalo sie rozwinac adresu: {host}.", nameof(host));

        return addresses[0];
    }

    public void Dispose()
    {
        Stop();
        _client.Dispose();
    }
}
