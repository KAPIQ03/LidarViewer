using System.Numerics;

namespace LidarViewer.Core.Rendering;

/// <summary>
/// Kamera orbitalna: krąży po sferze wokół punktu <see cref="Target"/> i na niego patrzy.
/// Logika jest w Core (bez OpenGL), dzięki czemu da się ją testować jednostkowo,
/// a widok 3D tylko pobiera gotowe macierze.
///
/// Układ współrzędnych: prawoskrętny, oś Z to "góra" (typowe dla lidaru).
/// Pozycja kamery = Target + Distance * (cos(pitch)cos(yaw), cos(pitch)sin(yaw), sin(pitch)).
/// Macierze są w konwencji System.Numerics (wektor wierszowy: v * M), a rzutowanie daje głębię
/// w zakresie [-1, 1] jak w OpenGL - po zapisaniu do uniformu bez transpozycji działają w shaderze.
/// </summary>
public sealed class OrbitCamera
{
    /// <summary>Maksymalne nachylenie (89°) - zapobiega przekręceniu kamery przez biegun.</summary>
    public const float MaxPitch = 89f * MathF.PI / 180f;

    /// <summary>Punkt, na który patrzy kamera.</summary>
    public Vector3 Target { get; set; }

    /// <summary>Obrót wokół osi Z (radiany).</summary>
    public float Yaw { get; set; } = MathF.PI / 4;

    /// <summary>Nachylenie nad płaszczyzną XY (radiany), ograniczone do ±MaxPitch.</summary>
    public float Pitch { get; set; } = MathF.PI / 6;

    /// <summary>Odległość kamery od celu.</summary>
    public float Distance { get; private set; } = 10f;

    /// <summary>Kąt widzenia w pionie (radiany).</summary>
    public float FovY { get; set; } = MathF.PI / 4;

    /// <summary>Promień sceny - wyznacza płaszczyzny odcinania i zakres zoomu.</summary>
    public float SceneRadius { get; private set; } = 10f;

    /// <summary>Ustawia kamerę tak, by cała kula o danym środku i promieniu mieściła się w kadrze.</summary>
    public void FitTo(Vector3 center, float radius)
    {
        SceneRadius = Math.Max(radius, 1e-3f);
        Target = center;
        Distance = SceneRadius * 2.5f;
    }

    /// <summary>Obraca kamerę wokół celu (zmiana yaw i pitch w radianach).</summary>
    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -MaxPitch, MaxPitch);
    }

    /// <summary>Przybliża (factor &lt; 1) lub oddala (factor &gt; 1) kamerę, w granicach 5% - 50 promieni sceny.</summary>
    public void Zoom(float factor) =>
        Distance = Math.Clamp(Distance * factor, SceneRadius * 0.05f, SceneRadius * 50f);

    /// <summary>Przesuwa cel w płaszczyźnie ekranu: right/up w jednostkach świata.</summary>
    public void Pan(float right, float up)
    {
        Vector3 forward = Vector3.Normalize(Target - Position);
        Vector3 rightAxis = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 upAxis = Vector3.Cross(rightAxis, forward);
        Target += rightAxis * right + upAxis * up;
    }

    /// <summary>Ile jednostek świata przypada na jeden piksel ekranu (na głębokości celu) - do przeciągania.</summary>
    public float WorldUnitsPerPixel(int viewportHeight) =>
        2f * Distance * MathF.Tan(FovY / 2f) / Math.Max(viewportHeight, 1);

    /// <summary>Pozycja kamery w świecie.</summary>
    public Vector3 Position => Target + Distance * new Vector3(
        MathF.Cos(Pitch) * MathF.Cos(Yaw),
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch));

    /// <summary>Macierz widoku (świat -> kamera).</summary>
    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);

    /// <summary>Macierz rzutowania perspektywicznego w stylu OpenGL (głębia -1..1).</summary>
    public Matrix4x4 Projection(float aspect)
    {
        float near = Math.Max(SceneRadius * 0.001f, 1e-4f);
        float far = Distance + SceneRadius * 2f;
        float f = 1f / MathF.Tan(FovY / 2f);
        return new Matrix4x4(
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2f * far * near / (near - far), 0);
    }

    /// <summary>Macierz świat -> przestrzeń obcinania (widok * rzutowanie), przekazywana do shadera.</summary>
    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);
}
