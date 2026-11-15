using System.Text.Json;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Recording;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Zamienia przechwycenie sieciowe (pcap/pcapng) z czujnika Velodyne na pliki PLY z manifestem
/// (eksport do sekwencji). Do odtwarzania w aplikacji sluzy <see cref="PcapFrameSource"/> - bez kopii na dysku.
///
/// Pakiety danych (1206 B) sa dekodowane przez <see cref="VelodyneDecoder"/>;
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
    public static IEnumerable<(double Time, PointCloud Cloud)> ReadFrames(
        string pcapPath, VelodyneCalibration? calibration = null)
    {
        var source = new PcapFrameSource(pcapPath, calibration);
        for (int i = 0; i < source.FrameCount; i++)
            yield return (source.Times[i], source.LoadFrame(i));
    }

    /// <summary>
    /// Zapisuje klatki z pliku pcap jako sekwencje (frame_NNNNN.ply + sequence.json)
    /// w podanym katalogu i wczytuje ja.
    /// </summary>
    public static Sequence Import(
        string pcapPath, string outputDirectory, string? name = null, VelodyneCalibration? calibration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pcapPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        var frames = new List<object>();
        double previousTime = double.NegativeInfinity;
        foreach (var (time, cloud) in ReadFrames(pcapPath, calibration))
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
}
