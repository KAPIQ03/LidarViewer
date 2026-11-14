using System.Numerics;

namespace LidarViewer.Core.Spatial;

/// <summary>
/// Plaszczyzna zapisana w postaci n . x + d = 0.
/// </summary>
public readonly record struct PlaneModel(Vector3 Normal, float Offset)
{
    public float DistanceTo(Vector3 point) => MathF.Abs(Vector3.Dot(Normal, point) + Offset);

    /// <summary>
    /// Plaszczyzna przechodzaca przez trzy punkty. Zwraca false, gdy punkty
    /// sa wspolliniowe - takiej probki nie da sie uzyc w RANSAC.
    /// </summary>
    public static bool TryFromThreePoints(Vector3 a, Vector3 b, Vector3 c, out PlaneModel plane)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        float length = normal.Length();
        if (length < 1e-9f)
        {
            plane = default;
            return false;
        }

        normal /= length;
        plane = new PlaneModel(normal, -Vector3.Dot(normal, a));
        return true;
    }

    /// <summary>
    /// Plaszczyzna dopasowana metoda najmniejszych kwadratow. Normalna to
    /// wektor wlasny o najmniejszej wartosci wlasnej macierzy kowariancji,
    /// czyli kierunek o najmniejszym rozrzuceniu punktow.
    /// </summary>
    public static PlaneModel FitLeastSquares(IReadOnlyList<Vector3> points)
    {
        if (points.Count < 3)
            throw new ArgumentException("Potrzebne sa co najmniej trzy punkty.", nameof(points));

        Vector3 centroid = Vector3.Zero;
        for (int i = 0; i < points.Count; i++) centroid += points[i];
        centroid /= points.Count;

        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < points.Count; i++)
        {
            double dx = points[i].X - centroid.X;
            double dy = points[i].Y - centroid.Y;
            double dz = points[i].Z - centroid.Z;
            xx += dx * dx;
            xy += dx * dy;
            xz += dx * dz;
            yy += dy * dy;
            yz += dy * dz;
            zz += dz * dz;
        }

        double[] covariance = { xx, xy, xz, xy, yy, yz, xz, yz, zz };
        (double[] values, double[] vectors) = Jacobi(covariance);

        int smallest = 0;
        for (int i = 1; i < 3; i++) if (values[i] < values[smallest]) smallest = i;

        var normal = new Vector3(
            (float)vectors[smallest],
            (float)vectors[3 + smallest],
            (float)vectors[6 + smallest]);

        float length = normal.Length();
        if (length < 1e-9f) normal = new Vector3(0f, 0f, 1f);
        else normal /= length;

        return new PlaneModel(normal, -Vector3.Dot(normal, centroid));
    }

    /// <summary>
    /// Jacobiego metoda obrotow dla symetrycznej macierzy 3x3. Zwraca
    /// wartosci wlasne oraz wektory wlasne w kolumnach.
    /// </summary>
    private static (double[] values, double[] vectors) Jacobi(double[] matrix)
    {
        double[] a = (double[])matrix.Clone();
        var vectors = new double[9];
        for (int i = 0; i < 3; i++) vectors[i * 3 + i] = 1.0;

        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = 0;
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    off += Math.Abs(a[i * 3 + j]);

            if (off < 1e-20) break;

            for (int p = 0; p < 2; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    double apq = a[p * 3 + q];
                    if (Math.Abs(apq) < 1e-20) continue;

                    double tau = (a[q * 3 + q] - a[p * 3 + p]) / (2 * apq);
                    double t = (tau >= 0 ? 1.0 : -1.0) /
                               (Math.Abs(tau) + Math.Sqrt(tau * tau + 1));
                    double c = 1 / Math.Sqrt(1 + t * t);
                    double s = t * c;

                    // Obrot G na plaszczyznie (p, q): A' = G transpozycja A G.
                    double app = a[p * 3 + p];
                    double aqq = a[q * 3 + q];

                    for (int k = 0; k < 3; k++)
                    {
                        if (k == p || k == q) continue;

                        double akp = a[k * 3 + p];
                        double akq = a[k * 3 + q];
                        a[k * 3 + p] = c * akp - s * akq;
                        a[p * 3 + k] = a[k * 3 + p];
                        a[k * 3 + q] = s * akp + c * akq;
                        a[q * 3 + k] = a[k * 3 + q];
                    }

                    a[p * 3 + p] = c * c * app - 2 * s * c * apq + s * s * aqq;
                    a[q * 3 + q] = s * s * app + 2 * s * c * apq + c * c * aqq;
                    a[p * 3 + q] = c * s * (app - aqq) + (c * c - s * s) * apq;
                    a[q * 3 + p] = a[p * 3 + q];

                    for (int i = 0; i < 3; i++)
                    {
                        double vip = vectors[i * 3 + p];
                        double viq = vectors[i * 3 + q];
                        vectors[i * 3 + p] = c * vip - s * viq;
                        vectors[i * 3 + q] = s * vip + c * viq;
                    }
                }
            }
        }

        return ([a[0], a[4], a[8]], vectors);
    }
}
