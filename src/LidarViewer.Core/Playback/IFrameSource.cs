using LidarViewer.Core.Models;

namespace LidarViewer.Core.Playback;

public interface IFrameSource
{
    string Name { get; }

    int FrameCount { get; }

    IReadOnlyList<double> Times { get; }

    double Duration { get; }

    int FrameAt(double positionSeconds);

    PointCloud LoadFrame(int index);
}
