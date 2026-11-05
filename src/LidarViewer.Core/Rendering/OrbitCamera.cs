using System.Numerics;

namespace LidarViewer.Core.Rendering;

public sealed class OrbitCamera
{
    public const float MaxPitch = 89f * MathF.PI / 180f;

    public Vector3 Target { get; set; }

    public float Yaw { get; set; } = MathF.PI / 4;

    public float Pitch { get; set; } = MathF.PI / 6;

    public float Distance { get; private set; } = 10f;

    public float FovY { get; set; } = MathF.PI / 4;

    public float SceneRadius { get; private set; } = 10f;

    public void FitTo(Vector3 center, float radius)
    {
        SceneRadius = Math.Max(radius, 1e-3f);
        Target = center;
        Distance = SceneRadius * 2.5f;
    }

    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -MaxPitch, MaxPitch);
    }

    public void Zoom(float factor) =>
        Distance = Math.Clamp(Distance * factor, SceneRadius * 0.05f, SceneRadius * 50f);

    public void Pan(float right, float up)
    {
        Vector3 forward = Vector3.Normalize(Target - Position);
        Vector3 rightAxis = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 upAxis = Vector3.Cross(rightAxis, forward);
        Target += rightAxis * right + upAxis * up;
    }

    public float WorldUnitsPerPixel(int viewportHeight) =>
        2f * Distance * MathF.Tan(FovY / 2f) / Math.Max(viewportHeight, 1);

    public Vector3 Position => Target + Distance * new Vector3(
        MathF.Cos(Pitch) * MathF.Cos(Yaw),
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch));

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);

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

    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);
}
