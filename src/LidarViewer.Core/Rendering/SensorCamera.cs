using System.Numerics;

namespace LidarViewer.Core.Rendering;

/// <summary>
/// Kamera "z perspektywy lidaru": stoi w jednym miejscu (pozycja czujnika), a uzytkownik
/// obraca nia myszka (yaw/pitch) i zmienia kat widzenia. Uklad: Z do gory, yaw 0 patrzy wzdluz +X.
/// </summary>
public sealed class SensorCamera
{
    public const float MaxPitch = 89f * MathF.PI / 180f;
    public const float MinFov = 10f * MathF.PI / 180f;
    public const float MaxFov = 120f * MathF.PI / 180f;
    public const float DefaultFov = 70f * MathF.PI / 180f;

    public Vector3 Position { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public float FovY { get; set; } = DefaultFov;

    public float SceneRadius { get; private set; } = 10f;

    public void FitTo(Vector3 position, float radius)
    {
        Position = position;
        SceneRadius = Math.Max(radius, 1e-3f);
    }

    public void Reset()
    {
        Yaw = 0;
        Pitch = 0;
        FovY = DefaultFov;
    }

    /// <summary>
    /// Obraca kierunek patrzenia. Dodatnie deltaYaw skreca w lewo (w strone +Y), dodatnie deltaPitch w gore.
    /// </summary>
    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw = (Yaw + deltaYaw) % (2f * MathF.PI);
        Pitch = Math.Clamp(Pitch + deltaPitch, -MaxPitch, MaxPitch);
    }

    /// <summary>
    /// Zblizenie przez zmiane kata widzenia: factor &lt; 1 przybliza, &gt; 1 oddala.
    /// </summary>
    public void Zoom(float factor) => FovY = Math.Clamp(FovY * factor, MinFov, MaxFov);

    public Vector3 Forward => new(
        MathF.Cos(Pitch) * MathF.Cos(Yaw),
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch));

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitZ);

    public Matrix4x4 Projection(float aspect)
    {
        float near = Math.Max(SceneRadius * 0.0005f, 0.01f);
        float far = Math.Max(SceneRadius * 4f, near * 10f);
        float f = 1f / MathF.Tan(FovY / 2f);
        return new Matrix4x4(
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2f * far * near / (near - far), 0);
    }

    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);
}
