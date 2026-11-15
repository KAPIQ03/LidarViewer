# Etap 3 — Sekwencje chmur punktów: Implementation Plan

> **For agentic workers:** każde zadanie to jeden commit, TDD (RED → GREEN → refactor).

**Goal:** odtwarzanie sekwencji chmur punktów z katalogu jak film — play/pause, oś czasu, tempo, pętla.

**Architecture:** Core dostaje `Sequence` (model + walidacja manifestu) i `SequencePlayer` (niezależny od UI odtwarzacz czasu). App łączy to z istniejącym pipeline'em: bieżąca klatka jest źródłem filtrów, tak jak plik. Timer siedzi w code-behind okna, ViewModel udostępnia `Advance(TimeSpan)`.

**Tech Stack:** .NET 10, C#, xUnit, Avalonia 12.1.4, CommunityToolkit.Mvvm 8.4.2, `System.Text.Json`.

---

## Kontrakt API

```csharp
namespace LidarViewer.Core.Playback;

public sealed record SequenceFrame(string File, double? Timestamp);
public sealed record SequenceManifest(string? Name, IReadOnlyList<SequenceFrame> Frames);

public sealed class Sequence
{
    public Sequence(string directory, SequenceManifest manifest);
    public string Directory { get; }
    public string Name { get; }
    public IReadOnlyList<SequenceFrame> Frames { get; }
    public IReadOnlyList<double> Times { get; }
    public double Duration { get; }
    public int FrameAt(double positionSeconds);
    public PointCloud LoadFrame(int index);
}

public static class SequenceLoader
{
    public static Sequence Load(string directory);
}

public sealed class SequencePlayer
{
    public SequencePlayer(Sequence sequence);
    public Sequence Sequence { get; }
    public int FrameIndex { get; }
    public double PositionSeconds { get; }
    public bool IsPlaying { get; }
    public double Speed { get; set; }
    public bool Loop { get; set; }
    public event Action<int>? FrameChanged;
    public void Play();
    public void Pause();
    public void Toggle();
    public void Seek(double seconds);
    public void Advance(TimeSpan delta);
}
```

Reguły:
- brak `sequence.json` → `InvalidDataException("W katalogu nie ma pliku sequence.json.")`
- brak katalogu → `DirectoryNotFoundException("Katalog sekwencji nie istnieje: {ścieżka}.")`
- pusta lista klatek → `InvalidDataException("Sekwencja nie zawiera żadnej klatki.")`
- brak pliku klatki → `InvalidDataException("Brak pliku klatki: {ścieżka}.")`
- czas mniejszy od poprzedniego → `InvalidDataException("Casy klatek muszą rosnąć.")`
- brak `timestamp` → czas klatki to `indeks * 0.1`

---

### Task 1: Model sekwencji i loader manifestu

**Files:**
- Create: `src/LidarViewer.Core/Playback/SequenceManifest.cs`
- Create: `src/LidarViewer.Core/Playback/Sequence.cs`
- Create: `src/LidarViewer.Core/Playback/SequenceLoader.cs`
- Test: `tests/LidarViewer.Core.Tests/Playback/SequenceTests.cs`

- [ ] **Step 1: RED** — testy w `tests/LidarViewer.Core.Tests/Playback/SequenceTests.cs`:

```csharp
using LidarViewer.Core.Playback;

namespace LidarViewer.Core.Tests.Playback;

public class SequenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lvseq-" + Guid.NewGuid().ToString("N"));

    public SequenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void WriteFrame(string name, int count)
    {
        var lines = new List<string> { "ply", "format ascii 1.0", $"element vertex {count}", "property float x", "property float y", "property float z", "end_header" };
        for (int i = 0; i < count; i++) lines.Add($"{i} 0 0");
        File.WriteAllLines(Path.Combine(_dir, name), lines);
    }

    private void WriteManifest(string json) => File.WriteAllText(Path.Combine(_dir, "sequence.json"), json);

    [Fact]
    public void LoadReadsManifestWithTwoFrames()
    {
        WriteFrame("a.ply", 2);
        WriteFrame("b.ply", 3);
        WriteManifest("""{"name":"Test","frames":[{"file":"a.ply","timestamp":0.0},{"file":"b.ply","timestamp":0.25}]}""");

        var sequence = SequenceLoader.Load(_dir);

        Assert.Equal("Test", sequence.Name);
        Assert.Equal(2, sequence.Frames.Count);
        Assert.Equal([0.0, 0.25], sequence.Times);
    }

    [Fact]
    public void MissingManifestThrows()
    {
        var ex = Assert.Throws<InvalidDataException>(() => SequenceLoader.Load(_dir));
        Assert.Contains("sequence.json", ex.Message);
    }

    [Fact]
    public void MissingDirectoryThrows()
    {
        Assert.Throws<DirectoryNotFoundException>(() => SequenceLoader.Load(Path.Combine(_dir, "nie-ma")));
    }

    [Fact]
    public void EmptyFramesThrows()
    {
        WriteManifest("""{"name":"Test","frames":[]}""");
        Assert.Throws<InvalidDataException>(() => SequenceLoader.Load(_dir));
    }

    [Fact]
    public void MissingFrameFileThrows()
    {
        WriteManifest("""{"frames":[{"file":"brak.ply"}]}""");
        var ex = Assert.Throws<InvalidDataException>(() => SequenceLoader.Load(_dir));
        Assert.Contains("brak.ply", ex.Message);
    }

    [Fact]
    public void DecreasingTimestampsThrow()
    {
        WriteFrame("a.ply", 1);
        WriteFrame("b.ply", 1);
        WriteManifest("""{"frames":[{"file":"a.ply","timestamp":1.0},{"file":"b.ply","timestamp":0.5}]}""");
        Assert.Throws<InvalidDataException>(() => SequenceLoader.Load(_dir));
    }

    [Fact]
    public void MissingTimestampUsesDefaultInterval()
    {
        WriteFrame("a.ply", 1);
        WriteFrame("b.ply", 1);
        WriteManifest("""{"frames":[{"file":"a.ply"},{"file":"b.ply"}]}""");

        var sequence = SequenceLoader.Load(_dir);

        Assert.Equal([0.0, 0.1], sequence.Times);
    }

    [Fact]
    public void NameFallsBackToDirectoryName()
    {
        WriteFrame("a.ply", 1);
        WriteManifest("""{"frames":[{"file":"a.ply"}]}""");

        var sequence = SequenceLoader.Load(_dir);

        Assert.Equal(Path.GetFileName(_dir), sequence.Name);
    }

    [Fact]
    public void LoadFrameReturnsPointsFromFile()
    {
        WriteFrame("a.ply", 4);
        WriteManifest("""{"frames":[{"file":"a.ply"}]}""");

        var sequence = SequenceLoader.Load(_dir);

        Assert.Equal(4, sequence.LoadFrame(0).Count);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.09, 0)]
    [InlineData(0.1, 1)]
    [InlineData(0.9, 2)]
    public void FrameAtFindsFrameForPosition(double position, int expected)
    {
        WriteFrame("a.ply", 1);
        WriteFrame("b.ply", 1);
        WriteFrame("c.ply", 1);
        WriteManifest("""{"frames":[{"file":"a.ply","timestamp":0.0},{"file":"b.ply","timestamp":0.1},{"file":"c.ply","timestamp":0.2}]}""");

        var sequence = SequenceLoader.Load(_dir);

        Assert.Equal(expected, sequence.FrameAt(position));
    }
}
```

- [ ] **Step 2: Uruchom i potwierdź RED**

```bash
dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --nologo
```
Oczekiwane: błąd kompilacji — brak `LidarViewer.Core.Playback`.

- [ ] **Step 3: GREEN** — implementacja.

`src/LidarViewer.Core/Playback/SequenceManifest.cs`:

```csharp
namespace LidarViewer.Core.Playback;

public sealed record SequenceFrame(string File, double? Timestamp);

public sealed record SequenceManifest(string? Name, IReadOnlyList<SequenceFrame> Frames);
```

`src/LidarViewer.Core/Playback/Sequence.cs`:

```csharp
using LidarViewer.Core.Models;
using LidarViewer.Core.Readers;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Sekwencja chmur punktow - lista klatek z czasami. Klatki wczytuje sie
/// leniwie, ostatnia zostaje w pamieci, bo odtwarzanie czyta je po kolei.
/// </summary>
public sealed class Sequence
{
    private const double DefaultFrameInterval = 0.1;

    private int _cachedIndex = -1;
    private PointCloud? _cachedFrame;

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

    public IReadOnlyList<double> Times { get; }

    public double Duration { get; }

    /// <summary>
    /// Numer klatki dla podanego czasu. Ostatnia klatka trwa do konca
    /// sekwencji.
    /// </summary>
    public int FrameAt(double positionSeconds)
    {
        for (int i = 0; i < Frames.Count - 1; i++)
            if (positionSeconds < Times[i + 1]) return i;

        return Frames.Count - 1;
    }

    public PointCloud LoadFrame(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Frames.Count);

        if (_cachedIndex == index && _cachedFrame is not null)
            return _cachedFrame;

        string path = Path.Combine(Directory, Frames[index].File);
        PointCloud cloud = PointCloudLoader.Read(path);
        _cachedIndex = index;
        _cachedFrame = cloud;
        return cloud;
    }
}
```

`src/LidarViewer.Core/Playback/SequenceLoader.cs`:

```csharp
using System.Text.Json;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Wczytuje sekwencje z katalogu. Manifest lezy w pliku sequence.json,
/// klatki to pliki w tym samym katalogu.
/// </summary>
public static class SequenceLoader
{
    public const string ManifestFileName = "sequence.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Sequence Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Katalog sekwencji nie istnieje: {directory}.");

        string manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"W katalogu nie ma pliku {ManifestFileName}.");

        SequenceManifest manifest = ReadManifest(manifestPath);
        return new Sequence(directory, manifest);
    }

    private static SequenceManifest ReadManifest(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            ManifestDto? dto = JsonSerializer.Deserialize<ManifestDto>(json, Options);
            if (dto is null)
                throw new InvalidDataException($"Manifest {ManifestFileName} jest pusty.");

            var frames = dto.Frames
                .Where(f => !string.IsNullOrWhiteSpace(f.File))
                .Select(f => new SequenceFrame(f.File, f.Timestamp))
                .ToArray();

            return new SequenceManifest(dto.Name, frames);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Nie udało się odczytać {ManifestFileName}: {ex.Message}", ex);
        }
    }

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
```

- [ ] **Step 4: Uruchom testy — oczekiwane GREEN**

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.Core/Playback tests/LidarViewer.Core.Tests/Playback
git commit -m "feat(core): model sekwencji chmur punktow i loader manifestu"
```

---

### Task 2: Odtwarzacz

**Files:**
- Create: `src/LidarViewer.Core/Playback/SequencePlayer.cs`
- Test: `tests/LidarViewer.Core.Tests/Playback/SequencePlayerTests.cs`

- [ ] **Step 1: RED**

```csharp
using LidarViewer.Core.Playback;

namespace LidarViewer.Core.Tests.Playback;

public class SequencePlayerTests
{
    private static Sequence ThreeFrames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lvplayer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (string name in new[] { "a.ply", "b.ply", "c.ply" })
        {
            var lines = new List<string> { "ply", "format ascii 1.0", "element vertex 1", "property float x", "property float y", "property float z", "end_header", "0 0 0" };
            File.WriteAllLines(Path.Combine(dir, name), lines);
        }

        File.WriteAllText(Path.Combine(dir, "sequence.json"),
            """{"frames":[{"file":"a.ply","timestamp":0.0},{"file":"b.ply","timestamp":0.1},{"file":"c.ply","timestamp":0.2}]}""");

        return SequenceLoader.Load(dir);
    }

    [Fact]
    public void AdvanceDoesNothingWhenPaused()
    {
        var player = new SequencePlayer(ThreeFrames());

        player.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0, player.FrameIndex);
    }

    [Fact]
    public void AdvanceMovesToNextFrame()
    {
        var player = new SequencePlayer(ThreeFrames());
        player.Play();

        player.Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal(1, player.FrameIndex);
    }

    [Fact]
    public void SpeedMultipliesElapsedTime()
    {
        var player = new SequencePlayer(ThreeFrames()) { Speed = 2.0 };
        player.Play();

        player.Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal(2, player.FrameIndex);
    }

    [Fact]
    public void SpeedIsClampedToRange()
    {
        var player = new SequencePlayer(ThreeFrames()) { Speed = 10 };

        Assert.Equal(4.0, player.Speed);

        player.Speed = 0.01;

        Assert.Equal(0.25, player.Speed);
    }

    [Fact]
    public void SeekJumpsToFrame()
    {
        var player = new SequencePlayer(ThreeFrames());

        player.Seek(0.15);

        Assert.Equal(1, player.FrameIndex);
        Assert.Equal(0.15, player.PositionSeconds, 3);
    }

    [Fact]
    public void SeekIsClampedToDuration()
    {
        var player = new SequencePlayer(ThreeFrames());

        player.Seek(99);

        Assert.Equal(0.2, player.PositionSeconds, 3);
        Assert.Equal(2, player.FrameIndex);
    }

    [Fact]
    public void EndOfSequenceStopsWithoutLoop()
    {
        var player = new SequencePlayer(ThreeFrames());
        player.Play();

        player.Advance(TimeSpan.FromSeconds(1));

        Assert.False(player.IsPlaying);
        Assert.Equal(2, player.FrameIndex);
    }

    [Fact]
    public void EndOfSequenceRestartsWithLoop()
    {
        var player = new SequencePlayer(ThreeFrames()) { Loop = true };
        player.Play();

        player.Advance(TimeSpan.FromSeconds(1));

        Assert.True(player.IsPlaying);
        Assert.Equal(0, player.FrameIndex);
    }

    [Fact]
    public void FrameChangedFiresOnlyOnFrameChange()
    {
        var player = new SequencePlayer(ThreeFrames());
        int changes = 0;
        player.FrameChanged += _ => changes++;
        player.Play();

        player.Advance(TimeSpan.FromMilliseconds(50));
        player.Advance(TimeSpan.FromMilliseconds(50));

        Assert.Equal(1, changes);
    }
}
```

- [ ] **Step 2: RED** — brak `SequencePlayer`.

- [ ] **Step 3: GREEN** — `src/LidarViewer.Core/Playback/SequencePlayer.cs`:

```csharp
namespace LidarViewer.Core.Playback;

/// <summary>
/// Odtwarzacz sekwencji. Nie zna UI - czas podaje metoda Advance, a o
/// zmianie kadru informuje przez zdarzenie FrameChanged.
/// </summary>
public sealed class SequencePlayer
{
    private const double MinSpeed = 0.25;
    private const double MaxSpeed = 4.0;

    private readonly Sequence _sequence;
    private double _position;

    public SequencePlayer(Sequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        _sequence = sequence;
    }

    public Sequence Sequence => _sequence;

    public int FrameIndex { get; private set; }

    public double PositionSeconds => _position;

    public bool IsPlaying { get; private set; }

    public double Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, MinSpeed, MaxSpeed);
    }

    public bool Loop { get; set; }

    public event Action<int>? FrameChanged;

    private double _speed = 1.0;

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void Toggle() => IsPlaying = !IsPlaying;

    public void Seek(double seconds)
    {
        _position = Math.Clamp(seconds, 0, _sequence.Duration);
        SetFrame(_sequence.FrameAt(_position));
    }

    public void Advance(TimeSpan delta)
    {
        if (!IsPlaying)
            return;

        double target = _position + delta.TotalSeconds * Speed;

        if (target >= _sequence.Duration)
        {
            if (Loop)
            {
                target = 0;
            }
            else
            {
                target = _sequence.Duration;
                IsPlaying = false;
            }
        }

        _position = target;
        SetFrame(_sequence.FrameAt(_position));
    }

    private void SetFrame(int index)
    {
        if (index == FrameIndex)
            return;

        FrameIndex = index;
        FrameChanged?.Invoke(index);
    }
}
```

Uwaga: `Seek` przed pierwszym `Play` musi zmieniać kadr, a `SetFrame` pomija
zgłoszenie, gdy indeks się nie zmienił — przy `Seek` do tej samej klatki
zdarzenie nie fire'uje, ale `FrameIndex` się nie zmienia, więc nie ma problemu.

Uwaga do testu `EndOfSequenceRestartsWithLoop`: `Loop` ustawiane jest przez
inicjalizator obiektu, więc właściwość musi mieć `set` publiczne.

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.Core/Playback/SequencePlayer.cs tests/LidarViewer.Core.Tests/Playback/SequencePlayerTests.cs
git commit -m "feat(core): odtwarzacz sekwencji z osia czasu"
```

---

### Task 3: Integracja z MainViewModel

**Files:**
- Modify: `src/LidarViewer.App/ViewModels/MainViewModel.cs`
- Test: `tests/LidarViewer.App.Tests/SequencePlaybackTests.cs`

- [ ] **Step 1: RED**

```csharp
using LidarViewer.App.ViewModels;
using LidarViewer.Core.Playback;

namespace LidarViewer.App.Tests;

public class SequencePlaybackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lvapp-" + Guid.NewGuid().ToString("N"));

    public SequencePlaybackTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void WriteSequence(int frames = 3)
    {
        var entries = new List<string>();
        for (int i = 0; i < frames; i++)
        {
            var lines = new List<string> { "ply", "format ascii 1.0", "element vertex 2", "property float x", "property float y", "property float z", "end_header", "0 0 0", "1 1 1" };
            File.WriteAllLines(Path.Combine(_dir, $"f{i}.ply"), lines);
            entries.Add($"{{\"file\":\"f{i}.ply\",\"timestamp\":{i * 0.1}}}");
        }

        File.WriteAllText(Path.Combine(_dir, "sequence.json"), "{\"frames\":[" + string.Join(",", entries) + "]}");
    }

    [Fact]
    public void LoadingSequenceShowsFirstFrame()
    {
        WriteSequence();
        var vm = new MainViewModel();

        vm.LoadSequenceCommand.Execute(_dir);

        Assert.True(vm.HasSequence);
        Assert.Equal(2, vm.PointCloud?.Count);
        Assert.Equal("Klatka 1 / 3", vm.FrameInfo);
    }

    [Fact]
    public void PlayPauseTogglesPlayback()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);

        vm.PlayPauseCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        vm.PlayPauseCommand.Execute(null);
        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public void AdvanceMovesToNextFrameAndUpdatesCloud()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);
        vm.PlayPauseCommand.Execute(null);

        vm.Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal(1, vm.Player?.FrameIndex);
        Assert.Equal("Klatka 2 / 3", vm.FrameInfo);
    }

    [Fact]
    public void SeekChangesFrame()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);

        vm.Seek(0.15);

        Assert.Equal(1, vm.Player?.FrameIndex);
    }

    [Fact]
    public void FailedLoadKeepsApplicationUsable()
    {
        var vm = new MainViewModel();

        vm.LoadSequenceCommand.Execute(_dir);

        Assert.False(vm.HasSequence);
        Assert.Contains("sequence.json", vm.Status);
    }
}
```

- [ ] **Step 2: RED** — brak `LoadSequenceCommand` itd.

- [ ] **Step 3: GREEN** — dopisz do `MainViewModel`:

```csharp
using LidarViewer.Core.Playback;
```

pola:

```csharp
    /// <summary>
    /// Wczytana sekwencja klatek. null, gdy aplikacja pokazuje pojedynczy
    /// plik.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequence))]
    [NotifyPropertyChangedFor(nameof(FrameInfo))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public partial Sequence? Sequence { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequence))]
    [NotifyPropertyChangedFor(nameof(FrameInfo))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public partial SequencePlayer? Player { get; set; }

    public bool HasSequence => Sequence is not null;

    public bool IsPlaying => Player?.IsPlaying ?? false;

    public double Duration => Sequence?.Duration ?? 0;

    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    public string FrameInfo => Sequence is null
        ? string.Empty
        : $"Klatka {Player?.FrameIndex + 1} / {Sequence.Frames.Count}";

    public IRelayCommand PlayPauseCommand { get; }

    public IRelayCommand<string> LoadSequenceCommand { get; }
```

konstruktor:

```csharp
    public MainViewModel()
    {
        Pipeline.PropertyChanged += OnPipelinePropertyChanged;
        PlayPauseCommand = new RelayCommand(PlayPauseCore);
        LoadSequenceCommand = new RelayCommand<string>(LoadSequenceCore);
    }
```

metody:

```csharp
    private void PlayPauseCore()
    {
        if (Player is null)
            return;

        Player.Toggle();
        OnPropertyChanged(nameof(IsPlaying));
    }

    private void LoadSequenceCore(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            var sequence = SequenceLoader.Load(directory);
            Sequence = sequence;
            Player = new SequencePlayer(sequence);
            Player.FrameChanged += OnFrameChanged;
            Pipeline.Reset();
            PositionSeconds = 0;
            ShowFrame(0);
            Status = $"Wczytano sekwencję „{sequence.Name}” ({sequence.Frames.Count} klatek).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    private void OnFrameChanged(int index) => ShowFrame(index);

    private void ShowFrame(int index)
    {
        if (Sequence is null || Player is null)
            return;

        PointCloud cloud = Sequence.LoadFrame(index);
        Pipeline.Source = cloud;
        PointCloud = Pipeline.Output;
        OnPropertyChanged(nameof(FrameInfo));
        OnPropertyChanged(nameof(PositionSeconds));
    }

    /// <summary>
    /// Przesuwa odtwarzanie. Wolane z timera okna.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        if (Player is null || !Player.IsPlaying)
            return;

        Player.Advance(delta);
        PositionSeconds = Player.PositionSeconds;
    }

    public void Seek(double seconds)
    {
        if (Player is null)
            return;

        Player.Seek(seconds);
        PositionSeconds = Player.PositionSeconds;
        OnPropertyChanged(nameof(FrameInfo));
    }
```

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.App/ViewModels/MainViewModel.cs tests/LidarViewer.App.Tests/SequencePlaybackTests.cs
git commit -m "feat(app): odtwarzanie sekwencji w modelu widoku"
```

---

### Task 4: Pasek sterowania w oknie

**Files:**
- Modify: `src/LidarViewer.App/Views/MainWindow.axaml`
- Modify: `src/LidarViewer.App/Views/MainWindow.axaml.cs`

- [ ] **Step 1: GREEN — XAML**

Do lewej kolumny, pod paskiem narzędzi (przed `<TextBlock DockPanel.Dock="Bottom" Text="{Binding Status}" />`)
dodać pasek sterowania:

```xml
                <Border DockPanel.Dock="Bottom" Padding="8,6" Background="#14FFFFFF"
                        IsVisible="{Binding HasSequence}">
                    <StackPanel Spacing="6">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <Button Content="Odtwórz" Command="{Binding PlayPauseCommand}" />
                            <Slider Width="320" Minimum="0" Maximum="{Binding Duration}"
                                    Value="{Binding PositionSeconds, Mode=TwoWay}"
                                    TickFrequency="1" />
                            <TextBlock Text="{Binding FrameInfo}" VerticalAlignment="Center" MinWidth="90" />
                        </StackPanel>
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <TextBlock Text="Tempo" VerticalAlignment="Center" />
                            <ComboBox Width="90" SelectedIndex="2">
                                <ComboBoxItem Content="0.25x" />
                                <ComboBoxItem Content="0.5x" />
                                <ComboBoxItem Content="1x" />
                                <ComboBoxItem Content="2x" />
                                <ComboBoxItem Content="4x" />
                            </ComboBox>
                            <CheckBox Content="Pętla" IsChecked="{Binding Player.Loop}" />
                        </StackPanel>
                    </StackPanel>
                </Border>
```

- [ ] **Step 2: GREEN — code-behind**

W `MainWindow.axaml.cs` dodać:

```csharp
using System;
using System.ComponentModel;
using Avalonia.Threading;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DateTime _lastTick = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += OnTick;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlaying))
            _timer.IsEnabled = (DataContext as MainViewModel)?.IsPlaying ?? false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        DateTime now = DateTime.UtcNow;
        TimeSpan delta = now - _lastTick;
        _lastTick = now;

        vm.Advance(delta);
    }
```

oraz handler wyboru katalogu (przycisk obok pola ścieżki):

```csharp
    private async void OnBrowseSequenceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz katalog sekwencji",
        });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.LoadSequenceCommand.Execute(path);
    }
```

- [ ] **Step 3: Build + testy**

- [ ] **Step 4: Commit**

```bash
git add src/LidarViewer.App/Views/MainWindow.axaml src/LidarViewer.App/Views/MainWindow.axaml.cs
git commit -m "feat(app): pasek sterowania odtwarzaniem sekwencji"
```

---

### Task 5: Weryfikacja końcowa etapu 3

**Files:** brak zmian kodu (chyba że testy ujawnią błąd)

- [ ] **Step 1: Build** — `dotnet build LidarViewer.slnx -v q --nologo`, 0 błędów.
- [ ] **Step 2: Testy** — `dotnet test LidarViewer.slnx --nologo`, wszystkie zielone.
- [ ] **Step 3: Pokrycie** — Core >= 80%.
- [ ] **Step 4: Lista kontrolna** — sekwencja złożona z kilku plików `.ply` w katalogu
  (z `sequence.json`) wczytuje się, klatki się zmieniają, play/pause działa,
  oś czasu przewija, pętla wraca na początek, tempo działa.
- [ ] **Step 5: Commit** — tylko jeśli były poprawki.
