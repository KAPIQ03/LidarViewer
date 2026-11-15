using System.Buffers.Binary;
using LidarViewer.Core.Models;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Klatki z nagrania pcap czujnika Velodyne, czytane leniwie. Konstruktor skanuje plik raz i buduje
/// indeks (polozenie pakietow danych i granice obrotow wg przejscia azymutu przez 360 stopni);
/// sama klatka jest dekodowana dopiero na zadanie, a kilka ostatnich trzymanych w pamieci.
/// Dzieki temu nie powstaja zadne pliki posrednie, a przewijanie jest swobodne.
/// </summary>
public sealed class PcapFrameSource : IFrameSource
{
    private const int CacheSize = 8;

    private readonly string _path;
    private readonly VelodyneDecoder _decoder;
    private readonly long[] _offsets;
    private readonly double[] _packetTimes;
    private readonly int[] _frameStarts; // pierwszy pakiet kazdej klatki + wartownik na koncu
    private readonly double[] _times;

    private readonly object _gate = new();
    private readonly Dictionary<int, PointCloud> _cache = [];
    private readonly Queue<int> _cacheOrder = new();

    public PcapFrameSource(string path, VelodyneCalibration? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _decoder = new VelodyneDecoder(calibration ?? VelodyneCalibration.Vlp16);

        var offsets = new List<long>();
        var packetTimes = new List<double>();
        var frameStarts = new List<int>();

        double origin = 0;
        int lastAzimuth = -1;
        foreach (PcapPacket packet in PcapReader.ReadUdpPackets(path))
        {
            if (packet.Offset < 0 || packet.Payload.Length != VelodyneDecoder.PacketSize)
                continue;

            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload);
            if (header != VelodyneDecoder.UpperBank && header != VelodyneDecoder.LowerBank)
                continue;

            if (offsets.Count == 0)
                origin = packet.Time;

            int azimuth = BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(2, 2));
            if (offsets.Count == 0 || azimuth < lastAzimuth)
                frameStarts.Add(offsets.Count);

            lastAzimuth = azimuth;
            offsets.Add(packet.Offset);
            packetTimes.Add(packet.Time - origin);
        }

        if (offsets.Count == 0)
            throw new InvalidDataException(
                "W pliku nie znaleziono pakietów danych Velodyne (UDP, 1206 B).");

        frameStarts.Add(offsets.Count);
        _offsets = offsets.ToArray();
        _packetTimes = packetTimes.ToArray();
        _frameStarts = frameStarts.ToArray();

        // Czasy klatek musza scisle rosnac, takze przy wadliwych znacznikach w pliku.
        _times = new double[FrameCount];
        for (int i = 0; i < _times.Length; i++)
        {
            double time = _packetTimes[_frameStarts[i]];
            _times[i] = i > 0 && time <= _times[i - 1] ? _times[i - 1] + 1e-6 : time;
        }

        Name = Path.GetFileNameWithoutExtension(path);
    }

    public string Name { get; }

    public int FrameCount => _frameStarts.Length - 1;

    public IReadOnlyList<double> Times => _times;

    public double Duration => _times[^1];

    /// <summary>
    /// Liczba pakietow danych w pliku.
    /// </summary>
    public int PacketCount => _offsets.Length;

    public int FrameAt(double positionSeconds) => FrameTimes.IndexAt(_times, positionSeconds);

    public PointCloud LoadFrame(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, FrameCount);

        lock (_gate)
        {
            if (_cache.TryGetValue(index, out PointCloud? cached))
                return cached;
        }

        PointCloud cloud = Decode(index);

        lock (_gate)
        {
            if (_cache.TryAdd(index, cloud))
            {
                _cacheOrder.Enqueue(index);
                while (_cacheOrder.Count > CacheSize)
                    _cache.Remove(_cacheOrder.Dequeue());
            }
        }

        return cloud;
    }

    private PointCloud Decode(int index)
    {
        var builder = new CloudFrameBuilder();
        byte[] buffer = new byte[VelodyneDecoder.PacketSize];
        double frameStart = _packetTimes[_frameStarts[index]];

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        for (int p = _frameStarts[index]; p < _frameStarts[index + 1]; p++)
        {
            stream.Seek(_offsets[p], SeekOrigin.Begin);
            stream.ReadExactly(buffer);

            if (_decoder.TryDecode(buffer, out PointCloud? packetCloud) && packetCloud is not null)
                builder.Add(packetCloud, _packetTimes[p] - frameStart);
        }

        return builder.Take();
    }
}
