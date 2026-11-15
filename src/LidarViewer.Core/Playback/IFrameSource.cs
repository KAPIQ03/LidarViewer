using LidarViewer.Core.Models;

namespace LidarViewer.Core.Playback;

/// <summary>
/// Zrodlo klatek do odtwarzania: katalog z plikami (<see cref="Sequence"/>) albo
/// nagranie sieciowe czytane leniwie (<see cref="PcapFrameSource"/>).
/// </summary>
public interface IFrameSource
{
    string Name { get; }

    int FrameCount { get; }

    /// <summary>
    /// Czas poczatku kazdej klatki w sekundach, ściśle rosnący.
    /// </summary>
    IReadOnlyList<double> Times { get; }

    /// <summary>
    /// Czas ostatniej klatki - koniec osi czasu.
    /// </summary>
    double Duration { get; }

    /// <summary>
    /// Numer klatki widocznej w podanej chwili. Ostatnia klatka trwa do konca osi czasu.
    /// </summary>
    int FrameAt(double positionSeconds);

    PointCloud LoadFrame(int index);
}
