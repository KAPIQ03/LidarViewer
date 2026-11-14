namespace LidarViewer.App.ViewModels;

/// <summary>
/// Format pakietow UDP akceptowany w trybie live.
/// </summary>
public enum LiveProtocol
{
    /// <summary>
    /// Wlasny protokul aplikacji (LVLR) - uzywany przez symulator.
    /// </summary>
    Wlasny,

    /// <summary>
    /// Natywny format danych czujnika Velodyne VLP-16.
    /// </summary>
    VelodyneVlp16,
}
