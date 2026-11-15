using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;

namespace LidarViewer.Core.Recording;

public sealed class SequenceRecorder : IDisposable
{
    public const string FrameFilePrefix = "frame_";
    public const string FrameFileExtension = ".ply";

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

    public int FrameCount
    {
        get { lock (_sync) return _frames.Count; }
    }

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
