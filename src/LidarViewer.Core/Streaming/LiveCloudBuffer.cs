using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Kumulujacy bufor punktow o stalyj pojemnosci.
///
/// Tryb live pokazuje ostatnie pakiety, nie tylko ostatni, wiec punkty sie
/// kumuluja. Gdy bufor jest pelny, kasuje najstarsze - dzieki temu pamiec
/// nie rosie w nieskonczonosc, a widok pokazuje ostatni obrot czujnika.
///
/// Bufor nie jest bezpieczny watkowo - uzywa go tylko watek odbioru.
/// </summary>
public sealed class LiveCloudBuffer
{
    private readonly float[] _positions;
    private readonly byte[] _intensity;
    private int _writeIndex;
    private int _count;

    public LiveCloudBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _positions = new float[capacity * 3];
        _intensity = new byte[capacity];
    }

    public int Capacity => _intensity.Length;

    public int Count => _count;

    public void Add(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        // Przy pelnym buforze zostawiamy tylko najnowsze punkty dodawanej
        // chmury - starsze i tak zostalyby zaraz skasowane.
        int skip = Math.Max(0, cloud.Count - Capacity);
        int take = cloud.Count - skip;

        for (int i = 0; i < take; i++)
        {
            int source = (skip + i) * 3;
            int target = _writeIndex * 3;
            _positions[target] = cloud.Positions[source];
            _positions[target + 1] = cloud.Positions[source + 1];
            _positions[target + 2] = cloud.Positions[source + 2];
            _intensity[_writeIndex] = cloud.Intensity is { } intensity ? intensity[skip + i] : (byte)0;

            _writeIndex = (_writeIndex + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
    }

    public PointCloud Snapshot()
    {
        var positions = new float[_count * 3];
        var intensity = new byte[_count];

        if (_count == Capacity)
        {
            // Bufor jest pelny - najstarszy punkt lezy pod wskaznikiem zapisu.
            CopyFrom(_writeIndex, 0);
        }
        else
        {
            CopyFrom(0, 0);
        }

        return new PointCloud(positions, intensity);

        void CopyFrom(int start, int destination)
        {
            int first = Math.Min(_count, Capacity - start);
            Array.Copy(_positions, start * 3, positions, destination * 3, first * 3);
            Array.Copy(_intensity, start, intensity, destination, first);

            int rest = _count - first;
            if (rest <= 0)
                return;

            Array.Copy(_positions, 0, positions, (destination + first) * 3, rest * 3);
            Array.Copy(_intensity, 0, intensity, destination + first, rest);
        }
    }

    public void Clear()
    {
        _writeIndex = 0;
        _count = 0;
    }
}
