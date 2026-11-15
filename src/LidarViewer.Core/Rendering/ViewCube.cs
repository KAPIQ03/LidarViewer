using System.Numerics;

namespace LidarViewer.Core.Rendering;

public sealed record ViewCubeFace(Vector3 Axis, string Label, Vector2[] Corners, float Depth)
{
    public Vector3 LookDirection => -Axis;

    public Vector2 Center => (Corners[0] + Corners[1] + Corners[2] + Corners[3]) / 4f;
}

public static class ViewCube
{
    private static readonly (Vector3 Axis, string Label, Vector3 U, Vector3 V)[] Definitions =
    [
        (Vector3.UnitX, "X", Vector3.UnitY, Vector3.UnitZ),
        (-Vector3.UnitX, "-X", Vector3.UnitY, Vector3.UnitZ),
        (Vector3.UnitY, "Y", Vector3.UnitX, Vector3.UnitZ),
        (-Vector3.UnitY, "-Y", Vector3.UnitX, Vector3.UnitZ),
        (Vector3.UnitZ, "Z", Vector3.UnitX, Vector3.UnitY),
        (-Vector3.UnitZ, "-Z", Vector3.UnitX, Vector3.UnitY),
    ];

    public static IReadOnlyList<ViewCubeFace> Project(float yaw, float pitch, float size)
    {
        Vector3 eye = new(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitZ);

        float scale = size / 2f / 1.8f;
        var center = new Vector2(size / 2f, size / 2f);
        var faces = new List<ViewCubeFace>(3);

        foreach (var (axis, label, u, v) in Definitions)
        {
            if (Vector3.Dot(axis, eye) <= 1e-4f)
                continue;

            Vector3[] world =
            [
                axis - u - v, axis + u - v, axis + u + v, axis - u + v,
            ];
            var corners = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Vector3.Transform(world[i], view);
                corners[i] = new Vector2(center.X + p.X * scale, center.Y - p.Y * scale);
            }

            faces.Add(new ViewCubeFace(axis, label, corners, Vector3.Dot(axis, eye)));
        }

        faces.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        return faces;
    }

    public static ViewCubeFace? HitTest(IReadOnlyList<ViewCubeFace> faces, Vector2 point)
    {
        for (int i = faces.Count - 1; i >= 0; i--)
            if (Contains(faces[i].Corners, point))
                return faces[i];
        return null;
    }

    private static bool Contains(Vector2[] quad, Vector2 p)
    {
        bool? positive = null;
        for (int i = 0; i < quad.Length; i++)
        {
            Vector2 a = quad[i];
            Vector2 b = quad[(i + 1) % quad.Length];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (MathF.Abs(cross) < 1e-6f)
                continue;
            bool sign = cross > 0;
            positive ??= sign;
            if (positive != sign)
                return false;
        }

        return true;
    }
}
