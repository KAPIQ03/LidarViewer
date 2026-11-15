using LidarViewer.Core.Models;

namespace LidarViewer.Core.Rendering;

public enum ColorMode
{
    Solid,
    Intensity,
    Distance,
    Height,
    LaserId,
    Azimuth,
    Time,
}

public static class ColorModeExtensions
{
    /// <summary>
    /// Atrybut punktow, z ktorego liczony jest kolor. null dla trybu jednolitego.
    /// </summary>
    public static ScalarKind? ToScalarKind(this ColorMode mode) => mode switch
    {
        ColorMode.Intensity => ScalarKind.Intensity,
        ColorMode.Distance => ScalarKind.Distance,
        ColorMode.Height => ScalarKind.Height,
        ColorMode.LaserId => ScalarKind.LaserId,
        ColorMode.Azimuth => ScalarKind.Azimuth,
        ColorMode.Time => ScalarKind.Time,
        _ => null,
    };
}
