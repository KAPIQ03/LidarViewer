using System.Numerics;

namespace LidarViewer.Core.Rendering;

/// <summary>
/// Wybor punktu chmury myszka: znajduje punkt, ktorego rzut na ekran jest najblizej kursora.
/// </summary>
public static class PointPicker
{
    /// <summary>
    /// Zwraca indeks punktu najblizszego pozycji (screenX, screenY) w pikselach okna (Y rosnie w dol)
    /// albo -1, gdy zaden punkt nie lezy w promieniu tolerancePixels. Punkty poza bryla widzenia sa pomijane;
    /// przy rownej odleglosci wygrywa punkt blizszy kamerze.
    /// </summary>
    /// <param name="positions">XYZ w tym samym ukladzie, w ktorym dziala macierz viewProjection.</param>
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
