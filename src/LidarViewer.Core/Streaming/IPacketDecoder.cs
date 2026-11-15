using LidarViewer.Core.Models;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Dekoder pojedynczego pakietu UDP z chmura punktow.
/// </summary>
public interface IPacketDecoder
{
    /// <summary>
    /// Nazwa formatu - pokazywana uzytkownikowi.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Probuje zamienic pakiet na chmure punktow. Pakiet nieznanego
    /// formatu nie jest bledem - odbiornik po prostu go pomija.
    /// </summary>
    bool TryDecode(ReadOnlySpan<byte> packet, out PointCloud? cloud);
}
