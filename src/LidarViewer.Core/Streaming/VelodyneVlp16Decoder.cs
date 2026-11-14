using System.Buffers.Binary;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Dekoder pakietow danych czujnika Velodyne VLP-16.
///
/// Pakiet ma 1206 bajtow: 12 blokow po 100 bajtow oraz 6 bajtow naglowka.
/// Blok zaczyna sie od 2 bajtow naglowka banku laserow (0xEEFF dla VLP-16)
/// i 2 bajtow azymutu w dziesiatych stopnia, dalej 96 bajtow danych:
/// 2 strzalow x 16 laserow x (dystans uint16 w jednostkach 2 mm + refleksywnosc).
///
/// Uklad i stale pochodza ze sterownika ROS "ros-drivers/velodyne"
/// (velodyne_pointcloud/src/lib/rawdata.cc, params/VLP16db.yaml).
/// Korekty per-laser dla konkretnego egzemplarza czujnika sa pomijane.
/// </summary>
public sealed class VelodyneVlp16Decoder : IPacketDecoder
{
    public const int PacketSize = 1206;
    public const int BlockSize = 100;
    public const int BlocksPerPacket = 12;
    public const ushort UpperBank = 0xEEFF;

    private const int ScansPerBlock = 32;
    private const int ScanSize = 3;
    private const int DataOffset = 4;
    private const float DistanceResolution = 0.002f;
    private const float BlockDurationUs = 110.592f;
    private const float ScanOffsetUs = 2.304f;
    private const float FiringOffsetUs = 55.296f;

    // Katy pionowe laserow 0..15 w radianach (VLP16db.yaml).
    private static readonly float[] VerticalAngles =
    [
        -0.2617993877991494f, 0.017453292519943295f,
        -0.22689280275926285f, 0.05235987755982989f,
        -0.19198621771937624f, 0.08726646259971647f,
        -0.15707963267948966f, 0.12217304763960307f,
        -0.12217304763960307f, 0.15707963267948966f,
        -0.08726646259971647f, 0.19198621771937624f,
        -0.05235987755982989f, 0.22689280275926285f,
        -0.017453292519943295f, 0.2617993877991494f,
    ];

    public string Name => "Velodyne VLP-16";

    public bool TryDecode(ReadOnlySpan<byte> packet, out PointCloud? cloud)
    {
        cloud = null;

        if (packet.Length < PacketSize)
            return false;

        var positions = new List<float>(BlocksPerPacket * ScansPerBlock * 3);
        var intensity = new List<byte>(BlocksPerPacket * ScansPerBlock);

        int lastAzimuthDiff = 0;
        for (int block = 0; block < BlocksPerPacket; block++)
        {
            int offset = block * BlockSize;

            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset, 2));
            if (header != UpperBank)
                return false;

            ushort rotation = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset + 2, 2));
            int azimuthDiff = AzimuthDiff(packet, block, rotation, ref lastAzimuthDiff);

            for (int firing = 0; firing < 2; firing++)
            {
                for (int laser = 0; laser < 16; laser++)
                {
                    int scan = offset + DataOffset + (firing * 16 + laser) * ScanSize;
                    ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(scan, 2));

                    // Zerowy dystans oznacza brak powrotu - taki punkt nie istnieje.
                    if (raw == 0)
                        continue;

                    float distance = raw * DistanceResolution;
                    float fraction = (laser * ScanOffsetUs + firing * FiringOffsetUs) / BlockDurationUs;
                    float azimuth = (rotation + azimuthDiff * fraction) / 100f * MathF.PI / 180f;
                    float vertical = VerticalAngles[laser];

                    float cosVertical = MathF.Cos(vertical);
                    positions.Add(distance * MathF.Cos(azimuth) * cosVertical);
                    positions.Add(distance * MathF.Sin(azimuth) * cosVertical);
                    positions.Add(distance * MathF.Sin(vertical));
                    intensity.Add(packet[scan + 2]);
                }
            }
        }

        cloud = new PointCloud(positions.ToArray(), intensity.ToArray());
        return true;
    }

    /// <summary>
    /// Roznica azymutow miedzy blokami w dziesiatych stopnia. Przy przekro-
    /// czeniu 360 stopni różnica jest ujemna - wtedy korzystamy z poprzedniej.
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
