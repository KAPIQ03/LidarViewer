using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;

namespace LidarViewer.Core.Recording;

/// <summary>
/// Nagrywa klatki chmury punktow do katalogu w formacie, ktory aplikacja
/// potrafi odtworzyc: pliki PLY plus manifest sequence.json.
///
/// Klatki zapisuje watek w tle, wiec strumien live nie zwalnia, a zapis
/// duzej chmury nie blokuje interfejsu.
/// </summary>
public sealed class SequenceRecorder : IDisposable
{
    public const string FrameFilePrefix = "frame_";
    public const string FrameFileExtension = ".ply";

    /// <summary>
    /// Najmniejszy odstep miedzy czasami klatek. Manifest wymaga czasow
    /// rosnacych, wiec klatki dodane w tej samej chwili musza dostac
    /// rozne, chociaż minimalnie rozne, znaczniki czasu.
    /// </summary>
    private const double MinTimestampStep = 0.001;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly Channel<FrameTask> _queue =
        Channel.CreateUnbounded<FrameTask>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Task _worker;
    private readonly Stopwatch _clock = new();
    private readonly List<RecordedFrame> _frames = [];
    private readonly List<string> _writtenFiles = [];
    private readonly object _sync = new();
    private readonly double _minIntervalSeconds;

    private double _lastTimestamp;
    private double _lastAcceptedElapsed;
    private Exception? _failure;
    private bool _closed;

    public SequenceRecorder(string directory, string? name = null, double minIntervalSeconds = 0.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(minIntervalSeconds);

        Directory = directory;
        Name = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))
            : name;
        _minIntervalSeconds = minIntervalSeconds;

        System.IO.Directory.CreateDirectory(directory);
        _clock.Start();
        _worker = Task.Run(ProcessAsync);
    }

    public string Directory { get; }

    public string Name { get; }

    /// <summary>
    /// Liczba przyjetych klatek. Rosnie od razu po dodaniu, bo plik po
    /// stronie jest zapisywany asynchronicznie.
    /// </summary>
    public int FrameCount
    {
        get { lock (_sync) return _frames.Count; }
    }

    /// <summary>
    /// Dodaje klatke do nagrania. Zwraca false, gdy klatka przyszla za
    /// szybko i zostala pominieta - nagranie live ma pelne dziesiatki
    /// klatek na sekunde, a dysk tego nie udzwignie.
    /// </summary>
    public bool AddFrame(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        lock (_sync)
        {
            if (_closed)
                return false;

            double now = _clock.Elapsed.TotalSeconds;
            bool first = _frames.Count == 0;

            if (!first && now - _lastAcceptedElapsed < _minIntervalSeconds)
                return false;

            double timestamp = first ? now : Math.Max(now, _lastTimestamp + MinTimestampStep);
            _lastTimestamp = timestamp;
            _lastAcceptedElapsed = now;

            int index = _frames.Count;
            string file = $"{FrameFilePrefix}{index:D5}{FrameFileExtension}";
            _frames.Add(new RecordedFrame(file, timestamp));
            _queue.Writer.TryWrite(new FrameTask(cloud, file));

            return true;
        }
    }

    /// <summary>
    /// Kończy nagranie i zapisuje manifest. Zwraca ścieżkę do pliku
    /// sequence.json.
    /// </summary>
    public async Task<string> FinishAsync()
    {
        lock (_sync)
        {
            if (_closed)
                throw new InvalidOperationException("Nagrywanie zostało już zakończone.");

            _closed = true;
        }

        _queue.Writer.Complete();
        await _worker;

        ThrowIfFailed();

        string manifestPath = Path.Combine(Directory, SequenceLoader.ManifestFileName);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(BuildManifest(), Options));

        return manifestPath;
    }

    /// <summary>
    /// Przerywa nagranie i usuwa zapisane pliki. Uzywane przy anulowaniu,
    /// zeby nie zostawic polowicowego nagrania na dysku.
    /// </summary>
    public async Task AbortAsync()
    {
        lock (_sync)
        {
            if (_closed)
                return;

            _closed = true;
        }

        _queue.Writer.Complete();
        await _worker;

        string[] files;
        lock (_sync)
            files = _writtenFiles.ToArray();

        foreach (string file in files)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (IOException)
            {
                // Plik zajety przez inny proces - kasowanie nie powinno
                // przerzucac calej operacji.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose()
    {
        if (!_closed)
            _ = AbortAsync();
    }

    private ManifestDto BuildManifest() => new()
    {
        Name = Name,
        Frames = _frames
            .Select(f => new FrameDto { File = f.File, Timestamp = f.Timestamp })
            .ToList(),
    };

    private void ThrowIfFailed()
    {
        if (_failure is null)
            return;

        throw new IOException("Nagranie nie powiodło się.", _failure);
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (FrameTask task in _queue.Reader.ReadAllAsync())
            {
                string path = Path.Combine(Directory, task.File);
                PlyWriter.Write(path, task.Cloud);

                lock (_sync)
                    _writtenFiles.Add(path);
            }
        }
        catch (Exception ex)
        {
            _failure = ex;
        }
    }

    private sealed record FrameTask(PointCloud Cloud, string File);

    private sealed record RecordedFrame(string File, double Timestamp);

    private sealed class ManifestDto
    {
        public string? Name { get; set; }
        public List<FrameDto> Frames { get; set; } = [];
    }

    private sealed class FrameDto
    {
        public string File { get; set; } = "";
        public double? Timestamp { get; set; }
    }
}
