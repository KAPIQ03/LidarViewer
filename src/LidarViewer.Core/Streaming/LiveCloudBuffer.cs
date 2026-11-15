using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Kumulujacy bufor punktow o stalyj pojemnosci.
///
/// Tryb live pokazuje ostatnie pakiety, nie tylko ostatni, wiec punkty sie
/// kumuluja. Gdy bufor jest pelny, kasuje najstarsze - dzieki temu pamiec
/// nie rosie w nieskonczonosc, a widok pokazuje ostatni obrot czujnika.
///
/// Bufor nie jest bezpieczny watkowo. UdpCloudReceiver chroni zapis i odczyt migawki wspolna blokada;
/// odczyt samego Count z innego watku jest bezpieczny (pojedyncza liczba calkowita).
/// </summary>
public sealed class LiveCloudBuffer
{
    private readonly float[] _positions;
    private readonly byte[] _intensity;
    private readonly byte[] _laserId;
    private readonly float[] _azimuth;
    private readonly float[] _time;
    private bool _hasLaserId, _hasAzimuth, _hasTime;
    private int _writeIndex;
    private int _count;
    private readonly bool _byRevolution;
    private float _lastAzimuth = float.NaN;
    private PointCloud? _completed;

    /// <param name="capacity">Pojemnosc w punktach.</param>
    /// <param name="byRevolution">
    /// Dziel strumien na obroty czujnika (po azymucie): migawka to ostatni pelny obrot, a nie
    /// kilka nalozonych na siebie. Wymaga atrybutu Azimuth w chmurach (dekoder Velodyne).
    /// </param>
    public LiveCloudBuffer(int capacity, bool byRevolution = false)
    {
        _byRevolution = byRevolution;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _positions = new float[capacity * 3];
        _intensity = new byte[capacity];
        _laserId = new byte[capacity];
        _azimuth = new float[capacity];
        _time = new float[capacity];
    }

    public int Capacity => _intensity.Length;

    public int Count => _count;

    public void Add(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        if (_byRevolution && cloud.Azimuth is { Length: > 0 } azimuths)
        {
            // Azymut spadajacy o ponad pol obrotu to przejscie przez 360 stopni = poczatek nowego obrotu.
            int start = 0;
            float previous = _lastAzimuth;
            for (int i = 0; i < azimuths.Length; i++)
            {
                if (!float.IsNaN(previous) && previous - azimuths[i] > 180f)
                {
                    AddRange(cloud, start, i);
                    CompleteRevolution();
                    start = i;
                }

                previous = azimuths[i];
            }

            AddRange(cloud, start, cloud.Count);
            _lastAzimuth = previous;
            return;
        }

        AddRange(cloud, 0, cloud.Count);
    }

    private void CompleteRevolution()
    {
        if (_count > 0)
            _completed = SnapshotCurrent();

        _writeIndex = 0;
        _count = 0;
    }

    private void AddRange(PointCloud cloud, int from, int to)
    {
        if (to <= from)
            return;

        // Przy pelnym buforze zostawiamy tylko najnowsze punkty dodawanej
        // chmury - starsze i tak zostalyby zaraz skasowane.
        int skip = from + Math.Max(0, (to - from) - Capacity);
        int take = to - skip;

        _hasLaserId |= cloud.LaserId is not null;
        _hasAzimuth |= cloud.Azimuth is not null;
        _hasTime |= cloud.Time is not null;

        for (int i = 0; i < take; i++)
        {
            int source = (skip + i) * 3;
            int target = _writeIndex * 3;
            _positions[target] = cloud.Positions[source];
            _positions[target + 1] = cloud.Positions[source + 1];
            _positions[target + 2] = cloud.Positions[source + 2];
            _intensity[_writeIndex] = cloud.Intensity is { } intensity ? intensity[skip + i] : (byte)0;
            _laserId[_writeIndex] = cloud.LaserId is { } laser ? laser[skip + i] : (byte)0;
            _azimuth[_writeIndex] = cloud.Azimuth is { } azimuth ? azimuth[skip + i] : 0f;
            _time[_writeIndex] = cloud.Time is { } time ? time[skip + i] : 0f;

            _writeIndex = (_writeIndex + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
    }

    /// <summary>
    /// Migawka. W trybie obrotow: ostatni pelny obrot (do czasu pierwszego pelnego obrotu - to, co juz jest).
    /// </summary>
    public PointCloud Snapshot() => _byRevolution && _completed is not null ? _completed : SnapshotCurrent();

    private PointCloud SnapshotCurrent()
    {
        var positions = new float[_count * 3];
        var intensity = new byte[_count];
        var laserId = _hasLaserId ? new byte[_count] : null;
        var azimuth = _hasAzimuth ? new float[_count] : null;
        var time = _hasTime ? new float[_count] : null;

        // Bufor pelny - najstarszy punkt lezy pod wskaznikiem zapisu.
        int start = _count == Capacity ? _writeIndex : 0;
        int first = Math.Min(_count, Capacity - start);
        int rest = _count - first;

        CopyRange(start, 0, first);
        if (rest > 0)
            CopyRange(0, first, rest);

        return new PointCloud(positions, intensity) { LaserId = laserId, Azimuth = azimuth, Time = time };

        void CopyRange(int source, int destination, int length)
        {
            Array.Copy(_positions, source * 3, positions, destination * 3, length * 3);
            Array.Copy(_intensity, source, intensity, destination, length);
            if (laserId is not null) Array.Copy(_laserId, source, laserId, destination, length);
            if (azimuth is not null) Array.Copy(_azimuth, source, azimuth, destination, length);
            if (time is not null) Array.Copy(_time, source, time, destination, length);
        }
    }

    public void Clear()
    {
        _writeIndex = 0;
        _count = 0;
        _completed = null;
        _lastAzimuth = float.NaN;
        _hasLaserId = _hasAzimuth = _hasTime = false;
    }
}
