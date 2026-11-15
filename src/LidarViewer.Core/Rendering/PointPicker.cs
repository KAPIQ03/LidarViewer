using System.Numerics;

namespace LidarViewer.Core.Rendering;

public static class PointPicker
{
    public static int Pick(
        float[] positions, Matrix4x4 viewProjection, float screenX, float screenY,
        int viewportWidth, int viewportHeight, float tolerancePixels = 12f)
    {
        ArgumentNullException.ThrowIfNull(positions);

        int best = -1;
        float bestDistance = tolerancePixels * tolerancePixels;
        float bestDepth = float.MaxValue;

        for (int i = 0; i < positions.Length / 3; i++)
        {
            var clip = Vector4.Transform(new Vector4(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], 1f), viewProjection);
            if (clip.W <= 0f)
                continue;

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            float ndcZ = clip.Z / clip.W;
            if (ndcX is < -1f or > 1f || ndcY is < -1f or > 1f || ndcZ is < -1f or > 1f)
                continue;

            float px = (ndcX * 0.5f + 0.5f) * viewportWidth;
            float py = (1f - (ndcY * 0.5f + 0.5f)) * viewportHeight;
            float dx = px - screenX;
            float dy = py - screenY;
            float distance = dx * dx + dy * dy;

            if (distance < bestDistance || (distance == bestDistance && best >= 0 && clip.W < bestDepth))
            {
                best = i;
                bestDistance = distance;
                bestDepth = clip.W;
            }
        }

        return best;
    }
}
