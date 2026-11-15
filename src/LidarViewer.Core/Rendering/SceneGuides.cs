using System.Numerics;

namespace LidarViewer.Core.Rendering;

public sealed class SceneGuides
{
    private const int MaxRings = 40;
    private const int Spokes = 12;
    private const int RingSegments = 96;

    private SceneGuides(float[] axisPositions, byte[] axisColors, float[] gridPositions, byte[] gridColors, float ringStep, int ringCount)
    {
        AxisPositions = axisPositions;
        AxisColors = axisColors;
        GridPositions = gridPositions;
        GridColors = gridColors;
        RingStep = ringStep;
        RingCount = ringCount;
    }

    public float[] AxisPositions { get; }

    public byte[] AxisColors { get; }

    public float[] GridPositions { get; }

    public byte[] GridColors { get; }

    public int AxisVertexCount => AxisPositions.Length / 3;

    public int GridVertexCount => GridPositions.Length / 3;

    public float RingStep { get; }

    public int RingCount { get; }

    public static SceneGuides Build(Vector3 origin, float sceneRadius)
    {
        float radius = Math.Max(sceneRadius, 1e-3f);
        float step = NiceStep(radius);
        int rings = Math.Clamp((int)MathF.Ceiling(radius / step), 1, MaxRings);
        float outer = rings * step;

        float axisLength = Math.Max(step, radius * 0.1f);
        float[] axisPositions =
        [
            origin.X, origin.Y, origin.Z, origin.X + axisLength, origin.Y, origin.Z,
            origin.X, origin.Y, origin.Z, origin.X, origin.Y + axisLength, origin.Z,
            origin.X, origin.Y, origin.Z, origin.X, origin.Y, origin.Z + axisLength,
        ];
        byte[] axisColors =
        [
            230, 60, 60, 255, 230, 60, 60, 255,
            70, 210, 70, 255, 70, 210, 70, 255,
            80, 130, 240, 255, 80, 130, 240, 255,
        ];

        var positions = new List<float>();
        for (int ring = 1; ring <= rings; ring++)
        {
            float r = ring * step;
            for (int i = 0; i < RingSegments; i++)
            {
                float a0 = i * 2f * MathF.PI / RingSegments;
                float a1 = (i + 1) * 2f * MathF.PI / RingSegments;
                positions.AddRange([origin.X + r * MathF.Cos(a0), origin.Y + r * MathF.Sin(a0), origin.Z]);
                positions.AddRange([origin.X + r * MathF.Cos(a1), origin.Y + r * MathF.Sin(a1), origin.Z]);
            }
        }

        for (int i = 0; i < Spokes; i++)
        {
            float a = i * 2f * MathF.PI / Spokes;
            positions.AddRange([origin.X, origin.Y, origin.Z]);
            positions.AddRange([origin.X + outer * MathF.Cos(a), origin.Y + outer * MathF.Sin(a), origin.Z]);
        }

        byte[] gridColors = new byte[positions.Count / 3 * 4];
        Array.Fill(gridColors, (byte)90);
        for (int i = 3; i < gridColors.Length; i += 4)
            gridColors[i] = 255;

        return new SceneGuides(axisPositions, axisColors, positions.ToArray(), gridColors, step, rings);
    }

    public static float NiceStep(float sceneRadius)
    {
        float target = Math.Max(sceneRadius, 1e-3f) / 6f;
        float magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(target)));
        float normalized = target / magnitude;
        float nice = normalized < 1.5f ? 1f : normalized < 3.5f ? 2f : normalized < 7.5f ? 5f : 10f;
        return nice * magnitude;
    }
}
