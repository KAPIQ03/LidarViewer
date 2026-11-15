namespace LidarViewer.Core.Rendering;

public sealed record ColorSettings
{
    public static readonly ColorSettings Default = new();

    public ColorMode Mode { get; init; } = ColorMode.Solid;

    public ColormapKind Map { get; init; } = ColormapKind.Viridis;

    public float Min { get; init; }

    public float Max { get; init; } = 1f;

    public Rgba Solid { get; init; } = new(0xC4, 0xC4, 0xC4, 0xFF);

    public bool IsDegenerate => Max <= Min;
}
