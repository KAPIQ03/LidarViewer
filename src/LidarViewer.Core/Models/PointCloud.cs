using System.Numerics;

namespace LidarViewer.Core.Models;

/// <summary>
/// Chmura punktów w pamięci: jedna "klatka" danych z lidaru.
/// Pozycje są trzymane w płaskiej tablicy float (x0,y0,z0,x1,y1,z1,...),
/// bo taki układ można bez kopiowania wgrać do bufora OpenGL (VBO).
/// </summary>
public sealed class PointCloud
{
    /// <summary>Pozycje punktów: 3 floaty (X, Y, Z) na punkt.</summary>
    public float[] Positions { get; }

    /// <summary>Intensywność odbicia (0-255) na punkt; null, gdy plik jej nie zawiera.</summary>
    public byte[]? Intensity { get; }

    /// <summary>Kategoria punktu; null, gdy plik jej nie zawiera.</summary>
    public int[]? Category { get; }

    /// <summary>
    /// Początek układu współrzędnych (w jednostkach pliku, double). Format z dużymi
    /// współrzędnymi (np. LAS w układzie państwowym) zapisujemy jako pozycje float
    /// względem tego punktu, żeby nie tracić precyzji; rzeczywiste położenie = Origin + Positions.
    /// </summary>
    public (double X, double Y, double Z) Origin { get; init; }

    /// <summary>Liczba punktów w chmurze.</summary>
    public int Count => Positions.Length / 3;

    /// <summary>Minimalny narożnik prostopadłościanu otaczającego chmurę.</summary>
    public Vector3 BoundsMin { get; }

    /// <summary>Maksymalny narożnik prostopadłościanu otaczającego chmurę.</summary>
    public Vector3 BoundsMax { get; }

    public PointCloud(float[] positions, byte[]? intensity = null, int[]? category = null)
    {
        if (positions.Length % 3 != 0)
            throw new ArgumentException("Długość tablicy pozycji musi być wielokrotnością 3.", nameof(positions));

        Positions = positions;
        Intensity = intensity;
        Category = category;
        (BoundsMin, BoundsMax) = ComputeBounds(positions);
    }

    /// <summary>Środek prostopadłościanu otaczającego - używany do wyśrodkowania widoku.</summary>
    public Vector3 Center => (BoundsMin + BoundsMax) / 2f;

    /// <summary>Promień kuli opisanej na prostopadłościanie - używany do doboru odległości kamery.</summary>
    public float Radius => Vector3.Distance(BoundsMin, BoundsMax) / 2f;

    private static (Vector3 Min, Vector3 Max) ComputeBounds(float[] p)
    {
        if (p.Length == 0)
            return (Vector3.Zero, Vector3.Zero);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < p.Length; i += 3)
        {
            var v = new Vector3(p[i], p[i + 1], p[i + 2]);
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return (min, max);
    }

    /// <summary>
    /// Zwraca chmurę bez punktów o nieskończonych lub nieokreślonych (NaN) współrzędnych.
    /// Takie punkty zdarzają się w PCD/PLY/CSV (np. brak odbicia) i psułyby wyliczenie
    /// zakresu, a więc i widok. Gdy wszystkie punkty są poprawne, zwraca ten sam obiekt.
    /// </summary>
    public PointCloud WithoutInvalidPoints()
    {
        int valid = 0;
        for (int i = 0; i < Count; i++)
            if (IsValid(i)) valid++;
        if (valid == Count)
            return this;

        var positions = new float[valid * 3];
        var intensity = Intensity is null ? null : new byte[valid];
        var category = Category is null ? null : new int[valid];
        int o = 0;
        for (int i = 0; i < Count; i++)
        {
            if (!IsValid(i)) continue;
            Array.Copy(Positions, i * 3, positions, o * 3, 3);
            if (intensity != null) intensity[o] = Intensity![i];
            if (category != null) category[o] = Category![i];
            o++;
        }
        return new PointCloud(positions, intensity, category) { Origin = Origin };
    }

    private bool IsValid(int i) =>
        float.IsFinite(Positions[i * 3]) && float.IsFinite(Positions[i * 3 + 1]) && float.IsFinite(Positions[i * 3 + 2]);
}
