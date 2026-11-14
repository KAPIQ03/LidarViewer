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
            var lines = new List<string>
            {
                "ply", "format ascii 1.0", "element vertex 1",
                "property float x", "property float y", "property float z", "end_header", "0 0 0",
            };
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

    [Fact]
    public void ToggleSwitchesPlaybackState()
    {
        var player = new SequencePlayer(ThreeFrames());

        player.Toggle();
        Assert.True(player.IsPlaying);

        player.Toggle();
        Assert.False(player.IsPlaying);
    }
}
