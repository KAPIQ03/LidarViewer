using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;

namespace LidarViewer.Core.Tests.Playback;

public class SequencePlayerTests
{
    private static FakeFrameSource ThreeFrames() => new(3);

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

        Assert.Equal(0.1, player.Speed);
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

    [Fact]
    public void StepMovesByFramesPausesAndClamps()
    {
        var player = new SequencePlayer(ThreeFrames());
        player.Play();

        player.Step(1);
        Assert.Equal(1, player.FrameIndex);
        Assert.False(player.IsPlaying);

        player.Step(10);
        Assert.Equal(2, player.FrameIndex);

        player.Step(-1);
        Assert.Equal(1, player.FrameIndex);

        player.Step(-10);
        Assert.Equal(0, player.FrameIndex);
    }

    [Fact]
    public void GoToFirstAndLastJumpToRangeEdges()
    {
        var player = new SequencePlayer(ThreeFrames());

        player.GoToLast();
        Assert.Equal(2, player.FrameIndex);
        Assert.Equal(0.2, player.PositionSeconds, 6);

        player.GoToFirst();
        Assert.Equal(0, player.FrameIndex);
    }

    [Fact]
    public void RangeLimitsSeekAndPlayback()
    {
        var player = new SequencePlayer(ThreeFrames());

        player.SetRange(1, 1);

        Assert.Equal(1, player.FrameIndex);
        player.Seek(0.0);
        Assert.Equal(1, player.FrameIndex);
        player.Seek(5.0);
        Assert.Equal(1, player.FrameIndex);
    }

    [Fact]
    public void LoopWrapsToRangeStart()
    {
        var player = new SequencePlayer(ThreeFrames()) { Loop = true };
        player.SetRange(1, 2);
        player.Play();

        player.Advance(TimeSpan.FromMilliseconds(150));

        Assert.True(player.IsPlaying);
        Assert.Equal(1, player.FrameIndex);
        Assert.Equal(0.1, player.PositionSeconds, 6);
    }

    [Fact]
    public void PlaybackStopsAtRangeEnd()
    {
        var player = new SequencePlayer(ThreeFrames());
        player.SetRange(0, 1);
        player.Play();

        player.Advance(TimeSpan.FromSeconds(5));

        Assert.False(player.IsPlaying);
        Assert.Equal(1, player.FrameIndex);
    }

    [Fact]
    public void RangeIsClampedAndOrdered()
    {
        var player = new SequencePlayer(ThreeFrames());

        player.SetRange(5, 1);

        Assert.Equal(2, player.StartFrame);
        Assert.Equal(2, player.EndFrame);
    }

    [Fact]
    public void SpeedAllowsTenthOfRealTime()
    {
        var player = new SequencePlayer(ThreeFrames()) { Speed = 0.1 };

        Assert.Equal(0.1, player.Speed);
    }

    private sealed class FakeFrameSource(int count) : IFrameSource
    {
        public string Name => "test";

        public int FrameCount => count;

        public IReadOnlyList<double> Times { get; } = [.. Enumerable.Range(0, count).Select(i => i * 0.1)];

        public double Duration => Times[^1];

        public int FrameAt(double positionSeconds) => FrameTimes.IndexAt(Times, positionSeconds);

        public PointCloud LoadFrame(int index) => new([0f, 0f, 0f]);
    }
}
