using LidarViewer.Core.Models;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Sklada chmury z kolejnych pakietow jednego obrotu czujnika, razem z atrybutami punktow.
/// Czas punktu to przesuniecie pakietu wzgledem poczatku klatki plus czas wewnatrz pakietu.
/// </summary>
internal sealed class CloudFrameBuilder
{
    private readonly List<float> _positions = [];
    private readonly List<byte> _intensity = [];
    private readonly List<byte> _laserId = [];
    private readonly List<float> _azimuth = [];
    private readonly List<float> _time = [];

    public bool HasPoints => _positions.Count > 0;

    public void Add(PointCloud packetCloud, double packetOffsetSeconds)
    {
        _positions.AddRange(packetCloud.Positions);
        _intensity.AddRange(packetCloud.Intensity ?? new byte[packetCloud.Count]);
        _laserId.AddRange(packetCloud.LaserId ?? new byte[packetCloud.Count]);
        _azimuth.AddRange(packetCloud.Azimuth ?? new float[packetCloud.Count]);

        float[] inPacket = packetCloud.Time ?? new float[packetCloud.Count];
        foreach (float t in inPacket)
            _time.Add((float)packetOffsetSeconds + t);
    }

    public PointCloud Take()
    {
        var cloud = new PointCloud(_positions.ToArray(), _intensity.ToArray())
        {
            LaserId = _laserId.ToArray(),
            Azimuth = _azimuth.ToArray(),
            Time = _time.ToArray(),
        };

        _positions.Clear();
        _intensity.Clear();
        _laserId.Clear();
        _azimuth.Clear();
        _time.Clear();
        return cloud;
    }
}
