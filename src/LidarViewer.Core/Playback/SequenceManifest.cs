namespace LidarViewer.Core.Playback;

public sealed record SequenceFrame(string File, double? Timestamp);

public sealed record SequenceManifest(string? Name, IReadOnlyList<SequenceFrame> Frames);
