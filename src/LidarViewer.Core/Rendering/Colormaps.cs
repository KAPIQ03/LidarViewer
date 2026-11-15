namespace LidarViewer.Core.Rendering;

public static class Colormaps
{
    public const int DefaultSize = 256;

    public static IReadOnlyList<ColormapKind> All { get; } =
    [
        ColormapKind.Grayscale,
        ColormapKind.Viridis,
        ColormapKind.Turbo,
        ColormapKind.Jet,
    ];

    private static readonly (float T, byte R, byte G, byte B)[] ViridisAnchors =
    [
        (0.00f, 68, 1, 84),
        (0.10f, 72, 40, 120),
        (0.20f, 62, 74, 137),
        (0.30f, 49, 104, 142),
        (0.40f, 38, 130, 142),
        (0.50f, 31, 158, 137),
        (0.60f, 53, 183, 121),
        (0.70f, 109, 205, 89),
        (0.80f, 180, 222, 44),
        (0.90f, 222, 227, 24),
        (1.00f, 253, 231, 37),
    ];

    private static readonly (float T, byte R, byte G, byte B)[] TurboAnchors =
    [
        (0.00f, 48, 18, 59),
        (0.10f, 70, 134, 251),
        (0.20f, 57, 181, 229),
        (0.30f, 38, 229, 185),
        (0.40f, 48, 246, 113),
        (0.50f, 116, 252, 92),
        (0.60f, 188, 222, 38),
        (0.70f, 227, 163, 41),
        (0.80f, 252, 100, 18),
        (0.90f, 223, 42, 10),
        (1.00f, 122, 4, 3),
    ];

    private static readonly (float T, byte R, byte G, byte B)[] JetAnchors =
    [
        (0.00f, 0, 0, 127),
        (0.125f, 0, 0, 255),
        (0.375f, 0, 255, 255),
        (0.625f, 255, 255, 0),
        (0.875f, 255, 0, 0),
        (1.00f, 127, 0, 0),
    ];

    public static Rgb[] Build(ColormapKind kind, int size = DefaultSize)
    {
        if (size < 2)
            throw new ArgumentOutOfRangeException(nameof(size), "Tablica kolorow musi miec co najmniej 2 elementy.");

        var lut = new Rgb[size];
        for (int i = 0; i < size; i++)
        {
            float t = (float)i / (size - 1);
            lut[i] = kind switch
            {
                ColormapKind.Grayscale => Grayscale(t),
                ColormapKind.Viridis => FromAnchors(t, ViridisAnchors),
                ColormapKind.Turbo => FromAnchors(t, TurboAnchors),
                ColormapKind.Jet => FromAnchors(t, JetAnchors),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Nieznana mapa kolorow."),
            };
        }

        return lut;
    }

    private static Rgb Grayscale(float t)
    {
        byte v = (byte)Math.Clamp(MathF.Round(t * 255f), 0f, 255f);
        return new Rgb(v, v, v);
    }

    private static Rgb FromAnchors(float t, (float T, byte R, byte G, byte B)[] anchors)
    {
        if (t <= anchors[0].T)
            return new Rgb(anchors[0].R, anchors[0].G, anchors[0].B);

        for (int i = 1; i < anchors.Length; i++)
        {
            if (t > anchors[i].T) continue;

            var a = anchors[i - 1];
            var b = anchors[i];
            float span = b.T - a.T;
            float f = span <= 0f ? 0f : (t - a.T) / span;

            return new Rgb(
                (byte)Math.Clamp(MathF.Round(a.R + (b.R - a.R) * f), 0f, 255f),
                (byte)Math.Clamp(MathF.Round(a.G + (b.G - a.G) * f), 0f, 255f),
                (byte)Math.Clamp(MathF.Round(a.B + (b.B - a.B) * f), 0f, 255f));
        }

        var last = anchors[^1];
        return new Rgb(last.R, last.G, last.B);
    }
}
