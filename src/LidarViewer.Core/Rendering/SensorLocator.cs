using System.Numerics;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Rendering;

/// <summary>
/// Ustala, gdzie w chmurze jest czujnik (poczatek ukladu wspolrzednych swiata), aby osie i siatka
/// byly wokol niego. Pozycje w <see cref="PointCloud"/> sa wzgledem <see cref="PointCloud.Origin"/>
/// (np. LAS zapisuje przesuniecie ukladu), wiec poczatek swiata lezy w -Origin, a nie w (0,0,0).
/// </summary>
public static class SensorLocator
{
    /// <summary>
    /// Poczatek ukladu swiata we wspolrzednych chmury, jesli lezy wewnatrz jej otoczki (nagrania lidaru);
    /// w przeciwnym razie null (np. plik w ukladzie globalnym - wtedy uzywa sie srodka chmury).
    /// </summary>
    public static Vector3? Locate(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        var worldOrigin = new Vector3((float)-cloud.Origin.X, (float)-cloud.Origin.Y, (float)-cloud.Origin.Z);
        return Vector3.Distance(worldOrigin, cloud.Center) <= cloud.Radius ? worldOrigin : null;
    }
}
