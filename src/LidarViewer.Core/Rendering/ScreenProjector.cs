using System.Numerics;

namespace LidarViewer.Core.Rendering;

public static class ScreenProjector
{
    public static Vector2? Project(Vector3 point, Matrix4x4 viewProjection, float viewportWidth, float viewportHeight)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);
        if (clip.W <= 0f)
            return null;

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        float ndcZ = clip.Z / clip.W;
        if (ndcX is < -1f or > 1f || ndcY is < -1f or > 1f || ndcZ is < -1f or > 1f)
            return null;

        return new Vector2((ndcX * 0.5f + 0.5f) * viewportWidth, (1f - (ndcY * 0.5f + 0.5f)) * viewportHeight);
    }
}
