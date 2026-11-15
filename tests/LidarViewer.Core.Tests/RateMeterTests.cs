using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests;

public class RateMeterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CountsEventsInsideWindow()
    {
        var meter = new RateMeter();
        meter.Add(T0, 3);
        meter.Add(T0.AddMilliseconds(500), 2);

        Assert.Equal(5, meter.PerSecond(T0.AddMilliseconds(600)), 6);
    }

    [Fact]
    public void ForgetsEventsOlderThanWindow()
    {
        var meter = new RateMeter();
        meter.Add(T0, 10);

        Assert.Equal(0, meter.PerSecond(T0.AddSeconds(2)));
    }

    [Fact]
    public void LongerWindowAveragesPerSecond()
    {
        var meter = new RateMeter(TimeSpan.FromSeconds(2));
        meter.Add(T0, 10);

        Assert.Equal(5, meter.PerSecond(T0.AddSeconds(1)), 6);
    }

    [Fact]
    public void RejectsNonPositiveWindow() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateMeter(TimeSpan.Zero));
}
