namespace LidarViewer.Core.Streaming;

public sealed class VelodyneVlp16Decoder : VelodyneDecoder
{
    public VelodyneVlp16Decoder() : base(VelodyneCalibration.Vlp16)
    {
    }
}
