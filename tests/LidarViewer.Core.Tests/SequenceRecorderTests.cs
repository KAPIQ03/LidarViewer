using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Recording;

namespace LidarViewer.Core.Tests;

public class SequenceRecorderTests : TempDirTestBase
{
    private static PointCloud Cloud(int points = 3) =>
        new([.. Enumerable.Range(0, points).SelectMany(i => new[] { (float)i, 1f, 2f })]);

    private string TargetDirectory => Path.Combine(Dir, "nagranie");

    [Fact]
    public async Task FinishAsync_WritesManifestAndFrames()
    {
        using var recorder = new SequenceRecorder(TargetDirectory, "moje nagranie");
        recorder.AddFrame(Cloud());
        recorder.AddFrame(Cloud(5));

        string manifestPath = await recorder.FinishAsync();

        Assert.Equal(Path.Combine(TargetDirectory, "sequence.json"), manifestPath);
        Assert.True(File.Exists(manifestPath));
        Assert.Equal(2, recorder.FrameCount);
    }

    [Fact]
    public async Task FinishAsync_ManifestIsPlayableBySequenceLoader()
    {
        using var recorder = new SequenceRecorder(TargetDirectory, "nagranie");
        recorder.AddFrame(Cloud(3));
        recorder.AddFrame(Cloud(4));

        await recorder.FinishAsync();

        Sequence sequence = SequenceLoader.Load(TargetDirectory);

        Assert.Equal(2, sequence.Frames.Count);
        Assert.Equal("nagranie", sequence.Name);
        Assert.Equal(3, sequence.LoadFrame(0).Count);
        Assert.Equal(4, sequence.LoadFrame(1).Count);
    }

    [Fact]
    public async Task FinishAsync_FramesKeepIntensity()
    {
        using var recorder = new SequenceRecorder(TargetDirectory);
        recorder.AddFrame(new PointCloud([1f, 2f, 3f], [77]));

        await recorder.FinishAsync();

        Sequence sequence = SequenceLoader.Load(TargetDirectory);
        Assert.Equal(new byte[] { 77 }, sequence.LoadFrame(0).Intensity);
    }

    [Fact]
    public async Task FinishAsync_TimestampsGrowStrictly()
    {
        using var recorder = new SequenceRecorder(TargetDirectory);
        for (int i = 0; i < 20; i++)
            recorder.AddFrame(Cloud());

        await recorder.FinishAsync();

        Sequence sequence = SequenceLoader.Load(TargetDirectory);
        Assert.Equal(20, sequence.Times.Count);

        for (int i = 1; i < sequence.Times.Count; i++)
            Assert.True(sequence.Times[i] > sequence.Times[i - 1], $"Czas klatki {i} nie jest wiekszy od poprzedniej.");
    }

    [Fact]
    public void AddFrame_DropsFramesCloserThanInterval()
    {
        using var recorder = new SequenceRecorder(TargetDirectory, null, minIntervalSeconds: 5.0);

        Assert.True(recorder.AddFrame(Cloud()));
        Assert.False(recorder.AddFrame(Cloud()));
        Assert.Equal(1, recorder.FrameCount);
    }

    [Fact]
    public async Task AbortAsync_RemovesWrittenFrames()
    {
        var recorder = new SequenceRecorder(TargetDirectory);
        recorder.AddFrame(Cloud());

        await recorder.AbortAsync();

        Assert.Empty(Directory.GetFiles(TargetDirectory));
    }

    [Fact]
    public async Task FinishAsync_CalledTwice_Throws()
    {
        using var recorder = new SequenceRecorder(TargetDirectory);
        recorder.AddFrame(Cloud());
        await recorder.FinishAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.FinishAsync());
    }
}
