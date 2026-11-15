using LidarViewer.Core.Models;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Sekwencja chmur punktow - lista klatek z czasami. Klatki wczytuje sie
/// leniwie, ostatnia zostaje w pamieci, bo odtwarzanie czyta je po kolei.
/// </summary>
public sealed class Sequence : IFrameSource
{
    private const double DefaultFrameInterval = 0.1;
    private const int CacheSize = 16;

    private readonly FrameCache _cache = new(CacheSize);

    public Sequence(string directory, SequenceManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Frames.Count == 0)
            throw new InvalidDataException("Sekwencja nie zawiera żadnej klatki.");

        Directory = directory;
        Name = string.IsNullOrWhiteSpace(manifest.Name)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))
            : manifest.Name!;
        Frames = manifest.Frames.ToArray();

        var times = new double[Frames.Count];
        for (int i = 0; i < Frames.Count; i++)
        {
            string path = Path.Combine(directory, Frames[i].File);
            if (!File.Exists(path))
                throw new InvalidDataException($"Brak pliku klatki: {path}.");

            double time = Frames[i].Timestamp ?? i * DefaultFrameInterval;
            if (i > 0 && time <= times[i - 1])
                throw new InvalidDataException("Casy klatek muszą rosnąć.");

            times[i] = time;
        }

        Times = times;
        Duration = times[^1];
    }

    public string Directory { get; }

    public string Name { get; }

    public IReadOnlyList<SequenceFrame> Frames { get; }

    /// <summary>
    /// Efektywne czasy klatek w sekundach - z manifestu albo wyliczone
    /// z domyslnym odstepem.
    /// </summary>
    public IReadOnlyList<double> Times { get; }

    public double Duration { get; }

    public int FrameCount => Frames.Count;

    /// <summary>
    /// Numer klatki dla podanego czasu. Ostatnia klatka trwa do konca
    /// sekwencji.
    /// </summary>
    public int FrameAt(double positionSeconds) => FrameTimes.IndexAt(Times, positionSeconds);

    public PointCloud LoadFrame(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Frames.Count);

        return _cache.GetOrLoad(index, i =>
            PointCloudLoader.Read(Path.Combine(Directory, Frames[i].File)));
    }
}
