namespace LidarViewer.Core.Streaming;

/// <summary>
/// Dekoder VLP-16 z wbudowana kalibracja. Szczegoly ukladu pakietu: <see cref="VelodyneDecoder"/>.
/// </summary>
public sealed class VelodyneVlp16Decoder : VelodyneDecoder
{
    public VelodyneVlp16Decoder() : base(VelodyneCalibration.Vlp16)
    {
    }
}
