namespace LidarViewer.Core.Playback;

/// <summary>
/// Pojedyncza klatka sekwencji - nazwa pliku i czas, w ktorym sie pojawia.
/// </summary>
public sealed record SequenceFrame(string File, double? Timestamp);

/// <summary>
/// Manifest sekwencji - lista klatek odczytana z pliku sequence.json.
/// </summary>
public sealed record SequenceManifest(string? Name, IReadOnlyList<SequenceFrame> Frames);
