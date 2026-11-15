using System.Text.Json;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Recording;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Playback;

public static class VelodynePcapImporter
{
    public static IEnumerable<(double Time, PointCloud Cloud)> ReadFrames(
        string pcapPath, VelodyneCalibration? calibration = null)
    {
        var source = new PcapFrameSource(pcapPath, calibration);
        for (int i = 0; i < source.FrameCount; i++)
            yield return (source.Times[i], source.LoadFrame(i));
    }

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
