using System.Buffers.Binary;
using System.Text.Json;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Recording;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Zamienia przechwycenie sieciowe (pcap/pcapng) z czujnika Velodyne VLP-16
/// na klatki chmur punktow - jedna klatka to jeden pelny obrot czujnika.
///
/// Pakiety danych (1206 B) sa dekodowane przez <see cref="VelodyneVlp16Decoder"/>;
/// pozostale datagramy (np. pakiety pozycji GPS) sa pomijane. Nowa klatka zaczyna sie,
/// gdy azymut pakietu jest mniejszy niz w poprzednim (przejscie przez 360 stopni).
/// Pierwsza i ostatnia klatka moga byc niepelnymi obrotami, jesli przechwytywanie
/// zaczelo sie albo skonczylo w srodku obrotu.
/// </summary>
public static class VelodynePcapImporter
{
    /// <summary>
    /// Klatki w kolejnosci przechwycenia. Czas jest liczony w sekundach od
    /// pierwszego pakietu danych w pliku.
    /// </summary>
    public static IEnumerable<(double Time, PointCloud Cloud)> ReadFrames(string pcapPath)
    {
        var decoder = new VelodyneVlp16Decoder();
        var positions = new List<float>();
        var intensity = new List<byte>();

        double origin = 0;
        double frameStart = 0;
        int lastAzimuth = -1;
        bool first = true;

        foreach (PcapPacket packet in PcapReader.ReadUdpPackets(pcapPath))
        {
            if (packet.Payload.Length != VelodyneVlp16Decoder.PacketSize)
                continue;

            if (!decoder.TryDecode(packet.Payload, out PointCloud? cloud) || cloud is null)
                continue;

            if (first)
            {
                origin = packet.Time;
                first = false;
            }

            int azimuth = BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(2, 2));
            if (lastAzimuth >= 0 && azimuth < lastAzimuth && positions.Count > 0)
            {
                yield return (frameStart, TakeFrame(positions, intensity));
                frameStart = packet.Time - origin;
            }

            lastAzimuth = azimuth;
            positions.AddRange(cloud.Positions);
            intensity.AddRange(cloud.Intensity ?? new byte[cloud.Count]);
        }

        if (positions.Count > 0)
            yield return (frameStart, TakeFrame(positions, intensity));
    }

    /// <summary>
    /// Zapisuje klatki z pliku pcap jako sekwencje (frame_NNNNN.ply + sequence.json)
    /// w podanym katalogu i wczytuje ja.
    /// </summary>
    public static Sequence Import(string pcapPath, string outputDirectory, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pcapPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        var frames = new List<object>();
        double previousTime = double.NegativeInfinity;
        foreach (var (time, cloud) in ReadFrames(pcapPath))
        {
            // Sekwencja wymaga rosnacych czasow - przy wadliwych znacznikach nieco je rozsuwamy.
            double safeTime = time > previousTime ? time : previousTime + 1e-6;
            previousTime = safeTime;

            string file = $"{SequenceRecorder.FrameFilePrefix}{frames.Count:D5}{SequenceRecorder.FrameFileExtension}";
            PlyWriter.Write(Path.Combine(outputDirectory, file), cloud);
            frames.Add(new { File = file, Timestamp = safeTime });
        }

        if (frames.Count == 0)
            throw new InvalidDataException("W pliku nie znaleziono pakietów danych Velodyne VLP-16 (UDP, 1206 B).");

        string json = JsonSerializer.Serialize(
            new { Name = name ?? Path.GetFileNameWithoutExtension(pcapPath), Frames = frames },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputDirectory, SequenceLoader.ManifestFileName), json);

        return SequenceLoader.Load(outputDirectory);
    }

    private static PointCloud TakeFrame(List<float> positions, List<byte> intensity)
    {
        var cloud = new PointCloud(positions.ToArray(), intensity.ToArray());
        positions.Clear();
        intensity.Clear();
        return cloud;
    }
}
