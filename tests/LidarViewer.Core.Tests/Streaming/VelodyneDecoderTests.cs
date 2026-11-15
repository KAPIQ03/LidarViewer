using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class VelodyneDecoderTests : TempDirTestBase
{
    private static byte[] WidePacket(ushort[] bankHeaders, ushort azimuth = 0, ushort distance = 1000)
    {
        byte[] packet = new byte[VelodyneDecoder.PacketSize];
        for (int block = 0; block < VelodyneDecoder.BlocksPerPacket; block++)
        {
            int offset = block * VelodyneDecoder.BlockSize;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset, 2), bankHeaders[block % bankHeaders.Length]);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset + 2, 2), azimuth);
            for (int channel = 0; channel < 32; channel++)
            {
                int scan = offset + 4 + channel * 3;
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(scan, 2), distance);
                packet[scan + 2] = (byte)(channel + 1);
            }
        }
        return packet;
    }

    private static byte[] Vlp16Packet(ushort azimuth, ushort distance = 1000)
    {
        byte[] packet = new byte[VelodyneDecoder.PacketSize];
        for (int block = 0; block < VelodyneDecoder.BlocksPerPacket; block++)
        {
            int offset = block * VelodyneDecoder.BlockSize;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset, 2), VelodyneDecoder.UpperBank);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset + 2, 2), azimuth);
            for (int scan = 0; scan < 32; scan++)
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset + 4 + scan * 3, 2), distance);
        }
        return packet;
    }

    private static string CalibrationXml(int lasers, double rotCorrection = 0, double distCorrectionCm = 0, double horizCm = 0)
    {
        var xml = new StringBuilder("<boost_serialization><DB><points_><count>" + lasers + "</count><item_version>1</item_version>");
        for (int i = 0; i < lasers; i++)
        {
            xml.Append(string.Create(CultureInfo.InvariantCulture,
                $"<item><px><id_>{i}</id_><rotCorrection_>{rotCorrection}</rotCorrection_><vertCorrection_>{i - lasers / 2}</vertCorrection_>" +
                $"<distCorrection_>{distCorrectionCm}</distCorrection_><vertOffsetCorrection_>0</vertOffsetCorrection_>" +
                $"<horizOffsetCorrection_>{horizCm}</horizOffsetCorrection_></px></item>"));
        }
        xml.Append("</points_></DB></boost_serialization>");
        return xml.ToString();
    }

    [Fact]
    public void AzimuthZeroLooksAlongPositiveY()
    {
        var decoder = new VelodyneVlp16Decoder();

        decoder.TryDecode(Vlp16Packet(azimuth: 0), out var cloud);

        Assert.True(cloud!.Positions[4] > 1.9f);
        Assert.Equal(0f, cloud.Positions[3], 3);
    }

    [Fact]
    public void AzimuthIncreasesClockwiseFromYTowardPositiveX()
    {
        var decoder = new VelodyneVlp16Decoder();

        decoder.TryDecode(Vlp16Packet(azimuth: 9000), out var cloud);

        Assert.True(cloud!.Positions[3] > 1.9f);
        Assert.Equal(0f, cloud.Positions[4], 3);
    }

    [Fact]
    public void DecodeFillsLaserAzimuthAndTimeAttributes()
    {
        var decoder = new VelodyneVlp16Decoder();

        decoder.TryDecode(Vlp16Packet(azimuth: 4500), out var cloud);

        Assert.Equal(12 * 32, cloud!.LaserId!.Length);
        Assert.Equal(0, cloud.LaserId[0]);
        Assert.Equal(1, cloud.LaserId[1]);
        Assert.Equal(45f, cloud.Azimuth![0], 2);
        Assert.Equal(0f, cloud.Time![0], 6);
        Assert.True(cloud.Time[^1] > cloud.Time[0]);
        Assert.NotNull(cloud.GetAttribute(ScalarKind.LaserId));
    }

    [Fact]
    public void Hdl32ELayoutProducesThirtyTwoLasersPerBlock()
    {
        var decoder = new VelodyneDecoder(VelodyneCalibration.Hdl32E);

        bool ok = decoder.TryDecode(WidePacket([VelodyneDecoder.UpperBank]), out var cloud);

        Assert.True(ok);
        Assert.Equal(12 * 32, cloud!.Count);
        Assert.Equal(31, cloud.LaserId![31]);
        Assert.Equal(32, cloud.Intensity![31]);
    }

    [Fact]
    public void Hdl64LowerBankUsesLasersFromThirtyTwo()
    {
        var calibration = new VelodyneCalibration("test64",
            Enumerable.Range(0, 64).Select(i => new LaserCalibration(0f, i - 32f)).ToArray());
        var decoder = new VelodyneDecoder(calibration);

        decoder.TryDecode(WidePacket([VelodyneDecoder.UpperBank, VelodyneDecoder.LowerBank]), out var cloud);

        Assert.Equal(12 * 32, cloud!.Count);
        Assert.Equal(0, cloud.LaserId![0]);
        Assert.Equal(32, cloud.LaserId[32]);
        Assert.Equal(63, cloud.LaserId[63]);
    }

    [Fact]
    public void Vlp16RejectsLowerBank()
    {
        var decoder = new VelodyneVlp16Decoder();

        Assert.False(decoder.TryDecode(WidePacket([VelodyneDecoder.LowerBank]), out _));
    }

    [Fact]
    public void CalibrationReaderParsesCorrections()
    {
        string path = Write("db.xml", CalibrationXml(32, rotCorrection: 2.5, distCorrectionCm: 150, horizCm: 2));

        var calibration = VelodyneCalibrationReader.Read(path);

        Assert.Equal(32, calibration.Lasers.Count);
        Assert.Equal("db", calibration.Name);
        Assert.Equal(-16f, calibration.Lasers[0].VerticalAngle);
        Assert.Equal(2.5f, calibration.Lasers[0].RotationCorrection);
        Assert.Equal(1.5f, calibration.Lasers[0].DistanceCorrection, 4);
        Assert.Equal(0.02f, calibration.Lasers[0].HorizontalOffset, 4);
    }

    [Fact]
    public void DistanceCorrectionIsAddedToRawDistance()
    {
        string path = Write("db.xml", CalibrationXml(32, distCorrectionCm: 100));
        var decoder = new VelodyneDecoder(VelodyneCalibrationReader.Read(path));

        decoder.TryDecode(WidePacket([VelodyneDecoder.UpperBank], distance: 1000), out var cloud);

        int index = 16 * 3;
        float radius = MathF.Sqrt(
            cloud!.Positions[index] * cloud.Positions[index] +
            cloud.Positions[index + 1] * cloud.Positions[index + 1] +
            cloud.Positions[index + 2] * cloud.Positions[index + 2]);
        Assert.Equal(3f, radius, 3);
    }

    [Fact]
    public void CalibrationReaderRejectsMissingLaserIds()
    {
        string xml = CalibrationXml(16).Replace("<id_>5</id_>", "<id_>50</id_>");
        string path = Write("bad.xml", xml);

        Assert.Throws<InvalidDataException>(() => VelodyneCalibrationReader.Read(path));
    }

    [Fact]
    public void CalibrationWithUnsupportedLaserCountThrows()
    {
        string path = Write("odd.xml", CalibrationXml(20));

        Assert.Throws<NotSupportedException>(() => VelodyneCalibrationReader.Read(path));
    }

    [Fact]
    public void FiltersAndBufferKeepPerPointAttributes()
    {
        var decoder = new VelodyneVlp16Decoder();
        decoder.TryDecode(Vlp16Packet(azimuth: 1000), out var cloud);

        var filtered = ScalarRangeFilter.Apply(cloud!, ScalarKind.LaserId, 0, 0);
        var buffer = new LiveCloudBuffer(1000);
        buffer.Add(cloud!);
        var snapshot = buffer.Snapshot();

        Assert.Equal(12 * 2, filtered.Count);
        Assert.All(filtered.LaserId!, id => Assert.Equal(0, id));
        Assert.Equal(filtered.Count, filtered.Azimuth!.Length);
        Assert.Equal(filtered.Count, filtered.Time!.Length);
        Assert.Equal(cloud!.LaserId, snapshot.LaserId);
        Assert.Equal(cloud.Azimuth, snapshot.Azimuth);
    }

    [Fact]
    public void ImporterGivesFramesTimeAndLaserAttributes()
    {
        string pcap = Write("t.pcap", PcapBytes(
        [
            (10.00, Vlp16Packet(0)), (10.05, Vlp16Packet(1800)), (10.10, Vlp16Packet(0)),
        ]));

        var frames = VelodynePcapImporter.ReadFrames(pcap).ToList();

        Assert.Equal(2, frames.Count);
        Assert.NotNull(frames[0].Cloud.LaserId);
        Assert.Equal(2 * 12 * 32, frames[0].Cloud.Count);
        Assert.True(frames[0].Cloud.Time!.Max() >= 0.05f);
        Assert.Equal(0.10, frames[1].Time, 6);
    }

    private static byte[] PcapBytes(IEnumerable<(double Time, byte[] Payload)> packets)
    {
        using var ms = new MemoryStream();
        void U32(uint v)
        {
            byte[] b = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            ms.Write(b);
        }

        U32(0xA1B2C3D4u); U32(0x00040002); U32(0); U32(0); U32(65535); U32(1);
        foreach (var (time, payload) in packets)
        {
            byte[] frame = new byte[14 + 20 + 8 + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), 0x0800);
            frame[14] = 0x45;
            frame[14 + 9] = 17;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(14 + 20 + 4), (ushort)(8 + payload.Length));
            payload.CopyTo(frame, 14 + 20 + 8);

            uint sec = (uint)Math.Floor(time);
            U32(sec); U32((uint)Math.Round((time - sec) * 1e6)); U32((uint)frame.Length); U32((uint)frame.Length);
            ms.Write(frame);
        }
        return ms.ToArray();
    }
}
