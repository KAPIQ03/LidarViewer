using System.Buffers.Binary;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class PcapReaderTests : TempDirTestBase
{
    private static byte[] VelodynePacket(ushort firstAzimuth)
    {
        byte[] packet = new byte[VelodyneVlp16Decoder.PacketSize];
        for (int block = 0; block < VelodyneVlp16Decoder.BlocksPerPacket; block++)
        {
            int offset = block * VelodyneVlp16Decoder.BlockSize;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset, 2), VelodyneVlp16Decoder.UpperBank);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset + 2, 2), (ushort)(firstAzimuth + block * 10));
            for (int scan = 0; scan < 32; scan++)
            {
                int at = offset + 4 + scan * 3;
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(at, 2), 1000);
                packet[at + 2] = 50;
            }
        }
        return packet;
    }

    private static byte[] EthernetFrame(byte[] payload, byte protocol = 17, ushort fragment = 0)
    {
        byte[] frame = new byte[14 + 20 + 8 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), 0x0800);
        int ip = 14;
        frame[ip] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(ip + 2), (ushort)(20 + 8 + payload.Length));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(ip + 6), fragment);
        frame[ip + 9] = protocol;
        int udp = ip + 20;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(udp + 4), (ushort)(8 + payload.Length));
        payload.CopyTo(frame, udp + 8);
        return frame;
    }

    private static byte[] ClassicPcap(IEnumerable<(double Time, byte[] Frame)> records, bool bigEndian = false, bool nano = false)
    {
        using var ms = new MemoryStream();
        void U32(uint v)
        {
            byte[] b = new byte[4];
            if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(b, v);
            else BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            ms.Write(b);
        }

        U32(nano ? 0xA1B23C4Du : 0xA1B2C3D4u);
        U32(0x00040002);
        U32(0); U32(0); U32(65535);
        U32(1);

        foreach (var (time, frame) in records)
        {
            uint sec = (uint)Math.Floor(time);
            uint frac = (uint)Math.Round((time - sec) * (nano ? 1e9 : 1e6));
            U32(sec); U32(frac); U32((uint)frame.Length); U32((uint)frame.Length);
            ms.Write(frame);
        }
        return ms.ToArray();
    }

    private static byte[] PcapNg(IEnumerable<(ulong Ticks, byte[] Frame)> records)
    {
        using var ms = new MemoryStream();
        void Block(uint type, byte[] body)
        {
            byte[] padded = new byte[(body.Length + 3) & ~3];
            body.CopyTo(padded, 0);
            uint total = (uint)(padded.Length + 12);
            byte[] b = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, type); ms.Write(b);
            BinaryPrimitives.WriteUInt32LittleEndian(b, total); ms.Write(b);
            ms.Write(padded);
            BinaryPrimitives.WriteUInt32LittleEndian(b, total); ms.Write(b);
        }

        byte[] shb = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(shb, 0x1A2B3C4D);
        BinaryPrimitives.WriteUInt16LittleEndian(shb.AsSpan(4), 1);
        BinaryPrimitives.WriteInt64LittleEndian(shb.AsSpan(8), -1);
        Block(0x0A0D0D0A, shb);

        byte[] idb = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(idb, 1);
        Block(1, idb);

        foreach (var (ticks, frame) in records)
        {
            byte[] epb = new byte[20 + frame.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(epb.AsSpan(4), (uint)(ticks >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(epb.AsSpan(8), (uint)ticks);
            BinaryPrimitives.WriteUInt32LittleEndian(epb.AsSpan(12), (uint)frame.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(epb.AsSpan(16), (uint)frame.Length);
            frame.CopyTo(epb, 20);
            Block(6, epb);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ReadsUdpPayloadAndTimeFromClassicPcap()
    {
        byte[] payload = [1, 2, 3, 4];
        string path = Write("a.pcap", ClassicPcap([(10.5, EthernetFrame(payload))]));

        var packets = PcapReader.ReadUdpPackets(path).ToList();

        var packet = Assert.Single(packets);
        Assert.Equal(payload, packet.Payload);
        Assert.Equal(10.5, packet.Time, 6);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReadsOtherByteOrdersAndNanosecondResolution(bool bigEndian, bool nano)
    {
        string path = Write("b.pcap", ClassicPcap([(3.25, EthernetFrame([9, 8]))], bigEndian, nano));

        var packet = Assert.Single(PcapReader.ReadUdpPackets(path));

        Assert.Equal([9, 8], packet.Payload);
        Assert.Equal(3.25, packet.Time, 6);
    }

    [Fact]
    public void SkipsNonUdpAndFragmentedPackets()
    {
        string path = Write("c.pcap", ClassicPcap(
        [
            (1, EthernetFrame([1], protocol: 6)),
            (2, EthernetFrame([2], fragment: 0x2000)),
            (3, EthernetFrame([3])),
        ]));

        var packet = Assert.Single(PcapReader.ReadUdpPackets(path));

        Assert.Equal([3], packet.Payload);
    }

    [Fact]
    public void ReadsPcapNgWithMicrosecondDefault()
    {
        string path = Write("d.pcapng", PcapNg([(2_500_000UL, EthernetFrame([7, 7]))]));

        var packet = Assert.Single(PcapReader.ReadUdpPackets(path));

        Assert.Equal([7, 7], packet.Payload);
        Assert.Equal(2.5, packet.Time, 6);
    }

    [Fact]
    public void TruncatedCaptureEndsWithoutError()
    {
        byte[] bytes = ClassicPcap([(1, EthernetFrame([1])), (2, EthernetFrame([2]))]);
        string path = Write("e.pcap", bytes[..^5]);

        var packets = PcapReader.ReadUdpPackets(path).ToList();

        Assert.Single(packets);
    }

    [Fact]
    public void UnknownFormatThrows()
    {
        string path = Write("f.pcap", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.Throws<InvalidDataException>(() => PcapReader.ReadUdpPackets(path).ToList());
    }

    [Fact]
    public void ImporterSplitsFramesAtAzimuthWrap()
    {
        var records = new List<(double, byte[])>();
        double t = 100;
        foreach (ushort az in new ushort[] { 0, 100, 200, 0, 100, 200 })
        {
            records.Add((t, EthernetFrame(VelodynePacket(az))));
            t += 0.05;
        }
        records.Insert(1, (100.01, EthernetFrame(new byte[512])));
        string path = Write("g.pcap", ClassicPcap(records));

        var frames = VelodynePcapImporter.ReadFrames(path).ToList();

        Assert.Equal(2, frames.Count);
        Assert.Equal(0, frames[0].Time, 6);
        Assert.Equal(0.15, frames[1].Time, 6);
        Assert.Equal(3 * 12 * 32, frames[0].Cloud.Count);
    }

    [Fact]
    public void ImportWritesLoadableSequence()
    {
        var records = new List<(double, byte[])>();
        double t = 5;
        foreach (ushort az in new ushort[] { 0, 100, 0, 100 })
        {
            records.Add((t, EthernetFrame(VelodynePacket(az))));
            t += 0.05;
        }
        string pcap = Write("h.pcap", ClassicPcap(records));
        string output = Path.Combine(Dir, "seq");

        var sequence = VelodynePcapImporter.Import(pcap, output, "test");

        Assert.Equal("test", sequence.Name);
        Assert.Equal(2, sequence.Frames.Count);
        Assert.Equal(2 * 12 * 32, sequence.LoadFrame(1).Count);
    }

    [Fact]
    public void ImportWithoutVelodynePacketsThrows()
    {
        string pcap = Write("i.pcap", ClassicPcap([(1, EthernetFrame([1, 2, 3]))]));

        Assert.Throws<InvalidDataException>(() => VelodynePcapImporter.Import(pcap, Path.Combine(Dir, "out")));
    }
}
