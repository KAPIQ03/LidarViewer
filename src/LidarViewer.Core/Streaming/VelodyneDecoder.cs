using System.Buffers.Binary;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Dekoder pakietow danych Velodyne (1206 B: 12 blokow po 100 B + 6 B znacznika czasu i fabryki).
/// Blok: naglowek banku (0xEEFF gorny, 0xDDFF dolny - HDL-64E), azymut w setnych stopnia
/// i kanaly po 3 B (dystans uint16 w jednostkach 2 mm + intensywnosc).
///
/// Uklad blokow zalezy od liczby laserow w kalibracji:
///  - 16 (VLP-16): blok = 2 strzaly x 16 laserow, azymut drugiego strzalu jest interpolowany;
///  - 32 (HDL-32E/VLP-32): blok = 32 lasery;
///  - 64 (HDL-64E): blok = 32 lasery gornego albo dolnego banku.
///
/// Uklad wspolrzednych jak w dokumentacji Velodyne i VeloView: X w prawo, Y do przodu,
/// Z do gory, azymut rosnie zgodnie z ruchem wskazowek zegara od osi Y. Stale ukladu i
/// czasow pochodza z ros-drivers/velodyne (rawdata.cc) i dokumentacji czujnikow; czasy
/// dla 32 i 64 laserow sa przyblizone (czas bloku).
/// </summary>
public class VelodyneDecoder : IPacketDecoder
{
    public const int PacketSize = 1206;
    public const int BlockSize = 100;
    public const int BlocksPerPacket = 12;
    public const ushort UpperBank = 0xEEFF;
    public const ushort LowerBank = 0xDDFF;

    private const int ChannelsPerBlock = 32;
    private const int ChannelSize = 3;
    private const int DataOffset = 4;
    private const float DistanceResolution = 0.002f;

    // VLP-16: czas bloku, odstep miedzy laserami w strzale i miedzy strzalami (mikrosekundy).
    private const float Vlp16BlockUs = 110.592f;
    private const float Vlp16LaserUs = 2.304f;
    private const float Vlp16FiringUs = 55.296f;

    // 32 i 64 lasery: czas jednego bloku (mikrosekundy), przyblizenie dla HDL-32E.
    private const float WideBlockUs = 46.080f;

    private readonly VelodyneCalibration _calibration;
    private readonly float[] _sinVertical;
    private readonly float[] _cosVertical;

    public VelodyneDecoder(VelodyneCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        _calibration = calibration;
        _sinVertical = new float[calibration.Lasers.Count];
        _cosVertical = new float[calibration.Lasers.Count];
        for (int i = 0; i < calibration.Lasers.Count; i++)
        {
            float radians = calibration.Lasers[i].VerticalAngle * MathF.PI / 180f;
            _sinVertical[i] = MathF.Sin(radians);
            _cosVertical[i] = MathF.Cos(radians);
        }
    }

    public string Name => $"Velodyne {_calibration.Name}";

    public VelodyneCalibration Calibration => _calibration;

    public bool TryDecode(ReadOnlySpan<byte> packet, out PointCloud? cloud)
    {
        cloud = null;

        if (packet.Length < PacketSize)
            return false;

        int lasers = _calibration.Lasers.Count;
        int capacity = BlocksPerPacket * ChannelsPerBlock;
        var positions = new List<float>(capacity * 3);
        var intensity = new List<byte>(capacity);
        var laserIds = new List<byte>(capacity);
        var azimuths = new List<float>(capacity);
        var times = new List<float>(capacity);

        int lastAzimuthDiff = 0;
        for (int block = 0; block < BlocksPerPacket; block++)
        {
            int offset = block * BlockSize;

            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset, 2));
            int bankOrigin;
            if (header == UpperBank)
                bankOrigin = 0;
            else if (header == LowerBank && lasers == 64)
                bankOrigin = 32;
            else
                return false;

            ushort rotation = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset + 2, 2));
            int azimuthDiff = AzimuthDiff(packet, block, rotation, ref lastAzimuthDiff);

            int firings = lasers == 16 ? 2 : 1;
            int perFiring = lasers == 16 ? 16 : ChannelsPerBlock;

            for (int firing = 0; firing < firings; firing++)
            {
                for (int channel = 0; channel < perFiring; channel++)
                {
                    int scan = offset + DataOffset + (firing * perFiring + channel) * ChannelSize;
                    ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(scan, 2));

                    // Zerowy dystans oznacza brak powrotu - taki punkt nie istnieje.
                    if (raw == 0)
                        continue;

                    int laser = bankOrigin + channel;
                    float timeUs;
                    float azimuthHundredths;
                    if (lasers == 16)
                    {
                        float inBlock = channel * Vlp16LaserUs + firing * Vlp16FiringUs;
                        timeUs = block * Vlp16BlockUs + inBlock;
                        azimuthHundredths = rotation + azimuthDiff * (inBlock / Vlp16BlockUs);
                    }
                    else
                    {
                        timeUs = block * WideBlockUs;
                        azimuthHundredths = rotation;
                    }

                    LaserCalibration calibration = _calibration.Lasers[laser];
                    float distance = raw * DistanceResolution + calibration.DistanceCorrection;
                    if (distance <= 0f)
                        continue;

                    float azimuthDegrees = azimuthHundredths / 100f;
                    float azimuth = (azimuthDegrees - calibration.RotationCorrection) * MathF.PI / 180f;
                    float sinAzimuth = MathF.Sin(azimuth);
                    float cosAzimuth = MathF.Cos(azimuth);
                    float horizontal = distance * _cosVertical[laser];

                    positions.Add(horizontal * sinAzimuth - calibration.HorizontalOffset * cosAzimuth);
                    positions.Add(horizontal * cosAzimuth + calibration.HorizontalOffset * sinAzimuth);
                    positions.Add(distance * _sinVertical[laser] + calibration.VerticalOffset * _cosVertical[laser]);
                    intensity.Add(packet[scan + 2]);
                    laserIds.Add((byte)laser);
                    azimuths.Add(WrapDegrees(azimuthDegrees));
                    times.Add(timeUs * 1e-6f);
                }
            }
        }

        cloud = new PointCloud(positions.ToArray(), intensity.ToArray())
        {
            LaserId = laserIds.ToArray(),
            Azimuth = azimuths.ToArray(),
            Time = times.ToArray(),
        };
        return true;
    }

    private static float WrapDegrees(float degrees) => degrees >= 360f ? degrees - 360f : degrees;

    /// <summary>
    /// Roznica azymutow miedzy blokami w setnych stopnia. Przy przekroczeniu 360 stopni roznica
    /// jest ujemna - wtedy korzystamy z poprzedniej.
    /// </summary>
    private static int AzimuthDiff(ReadOnlySpan<byte> packet, int block, ushort rotation, ref int lastDiff)
    {
        if (block >= BlocksPerPacket - 1)
            return lastDiff;

        int nextOffset = (block + 1) * BlockSize + 2;
        ushort next = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(nextOffset, 2));
        int raw = next - rotation;

        if (raw < 0)
            return lastDiff;

        lastDiff = raw;
        return raw;
    }
}
