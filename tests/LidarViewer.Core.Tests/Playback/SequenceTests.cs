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
        var lines = new List<string>
        {
            "ply", "format ascii 1.0", $"element vertex {count}",
            "property float x", "property float y", "property float z", "end_header",
        };
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

    [Fact]
    public void MalformedManifestThrows()
    {
        File.WriteAllText(Path.Combine(_dir, "sequence.json"), "{ to nie jest json");

        Assert.Throws<InvalidDataException>(() => SequenceLoader.Load(_dir));
    }
}
