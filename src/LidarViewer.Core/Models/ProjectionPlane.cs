namespace LidarViewer.Core.Models;

/// <summary>Płaszczyzna, na którą rzutowana jest chmura w widoku 2D.</summary>
public enum ProjectionPlane
{
    /// <summary>Widok z góry: oś X w poziomie, Y w pionie.</summary>
    XY,

    /// <summary>Widok z boku (od przodu): oś X w poziomie, Z w pionie.</summary>
    XZ,

    /// <summary>Widok z boku (od strony): oś Y w poziomie, Z w pionie.</summary>
    YZ,
}
