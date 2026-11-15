using System.Numerics;

namespace LidarViewer.Core.Rendering;

/// <summary>Jedna widoczna sciana kostki orientacji (jak w Blenderze).</summary>
/// <param name="Axis">Kierunek osi, na ktora wskazuje sciana (np. +X = (1,0,0)).</param>
/// <param name="Label">Etykieta: X, Y, Z, -X, -Y, -Z.</param>
/// <param name="Corners">Cztery rogi na ekranie (piksele, os Y w dol), po kolei po obwodzie.</param>
/// <param name="Depth">Glebokosc srodka sciany; wieksza = blizej widza.</param>
public sealed record ViewCubeFace(Vector3 Axis, string Label, Vector2[] Corners, float Depth)
{
    /// <summary>Kierunek, w ktorym patrzy kamera po kliknieciu sciany (od sciany do srodka).</summary>
    public Vector3 LookDirection => -Axis;

    public Vector2 Center => (Corners[0] + Corners[1] + Corners[2] + Corners[3]) / 4f;
}

/// <summary>
/// Rzut ortograficzny szescianu orientacji dla kamery orbitalnej o zadanym yaw/pitch.
/// Czysta matematyka bez UI: kontrolka tylko rysuje zwrocone sciany i wywoluje <see cref="HitTest"/>.
/// </summary>
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

    /// <summary>
    /// Zwraca widoczne sciany posortowane od najdalszej do najblizszej (kolejnosc rysowania).
    /// </summary>
    /// <param name="yaw">Yaw kamery orbitalnej (rad).</param>
    /// <param name="pitch">Pitch kamery orbitalnej (rad).</param>
    /// <param name="size">Bok kwadratu, w ktorym rysujemy kostke (piksele).</param>
    public static IReadOnlyList<ViewCubeFace> Project(float yaw, float pitch, float size)
    {
        Vector3 eye = new(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitZ);

        // Szescian o boku 2 ma przekatna ~3.46, wiec skala dobrana tak, by zawsze miescil sie w kwadracie.
        float scale = size / 2f / 1.8f;
        var center = new Vector2(size / 2f, size / 2f);
        var faces = new List<ViewCubeFace>(3);

        foreach (var (axis, label, u, v) in Definitions)
        {
            // Sciana jest widoczna, gdy jej normalna ma dodatni skladnik w kierunku kamery.
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

    /// <summary>Sciana pod punktem (piksele) albo null.</summary>
    public static ViewCubeFace? HitTest(IReadOnlyList<ViewCubeFace> faces, Vector2 point)
    {
        // Od najblizszej: sciany nie zaslaniaja sie w widocznym zbiorze, ale kolejnosc jest bezpieczna.
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
