using System.Buffers.Binary;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class PcapFrameSourceTests : TempDirTestBase
{
    private static byte[] Vlp16Packet(ushort azimuth, ushort distance)
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

    private static byte[] EthernetFrame(byte[] payload)
    {
        byte[] frame = new byte[14 + 20 + 8 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), 0x0800);
        frame[14] = 0x45;
        frame[14 + 9] = 17;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(14 + 20 + 4), (ushort)(8 + payload.Length));
        payload.CopyTo(frame, 14 + 20 + 8);
        return frame;
    }

    /// <summary>Trzy obroty po dwa pakiety; kazdy obrot ma inny dystans, wiec widac, czy klatka zostala odczytana z wlasciwego miejsca.</summary>
    private static List<(double Time, byte[] Payload)> ThreeRevolutions() =>
    [
        (100.00, Vlp16Packet(0, 500)), (100.05, Vlp16Packet(1800, 500)),
        (100.10, Vlp16Packet(0, 1000)), (100.15, Vlp16Packet(1800, 1000)),
        (100.20, Vlp16Packet(0, 1500)), (100.25, Vlp16Packet(1800, 1500)),
    ];

    private static byte[] Classic(IEnumerable<(double Time, byte[] Payload)> packets)
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
            byte[] frame = EthernetFrame(payload);
            uint sec = (uint)Math.Floor(time);
            U32(sec); U32((uint)Math.Round((time - sec) * 1e6)); U32((uint)frame.Length); U32((uint)frame.Length);
            ms.Write(frame);
        }
        return ms.ToArray();
    }

    private static byte[] PcapNg(IEnumerable<(double Time, byte[] Payload)> packets)
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

        foreach (var (time, payload) in packets)
        {
            byte[] frame = EthernetFrame(payload);
            ulong ticks = (ulong)Math.Round(time * 1e6);
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

    private static float Radius(Models.PointCloud cloud, int point)
    {
        float x = cloud.Positions[point * 3], y = cloud.Positions[point * 3 + 1], z = cloud.Positions[point * 3 + 2];
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexesFramesAndReadsEachFromItsOwnPlace(bool pcapNg)
    {
        byte[] bytes = pcapNg ? PcapNg(ThreeRevolutions()) : Classic(ThreeRevolutions());
        string path = Write(pcapNg ? "a.pcapng" : "a.pcap", bytes);

        var source = new PcapFrameSource(path);

        Assert.Equal(3, source.FrameCount);
        Assert.Equal(6, source.PacketCount);
        Assert.Equal(0.1, source.Times[1], 6);
        Assert.Equal(0.2, source.Duration, 6);

        // Odczyt w odwrotnej kolejnosci - dostep losowy dziala bez skanowania od poczatku.
        Assert.Equal(3f, Radius(source.LoadFrame(2), 0), 3);   // 1500 * 2 mm
        Assert.Equal(1f, Radius(source.LoadFrame(0), 0), 3);   // 500 * 2 mm
        Assert.Equal(2f, Radius(source.LoadFrame(1), 0), 3);   // 1000 * 2 mm
        Assert.Equal(2 * 12 * 32, source.LoadFrame(1).Count);
    }

    [Fact]
    public void FrameAtFindsFrameForPosition()
    {
        var source = new PcapFrameSource(Write("b.pcap", Classic(ThreeRevolutions())));

        Assert.Equal(0, source.FrameAt(-1));
        Assert.Equal(0, source.FrameAt(0.0999));
        Assert.Equal(1, source.FrameAt(0.1));
        Assert.Equal(2, source.FrameAt(99));
    }

    [Fact]
    public void FramesKeepLaserAzimuthAndTime()
    {
        var source = new PcapFrameSource(Write("c.pcap", Classic(ThreeRevolutions())));

        var cloud = source.LoadFrame(0);

        Assert.NotNull(cloud.LaserId);
        Assert.NotNull(cloud.Azimuth);
        Assert.Equal(cloud.Count, cloud.Time!.Length);
        Assert.True(cloud.Time.Max() >= 0.05f); // drugi pakiet jest 50 ms po pierwszym
    }

    [Fact]
    public void RepeatedLoadReturnsSameCachedFrame()
    {
        var source = new PcapFrameSource(Write("d.pcap", Classic(ThreeRevolutions())));

        Assert.Same(source.LoadFrame(1), source.LoadFrame(1));
    }

    [Fact]
    public void FileWithoutVelodynePacketsThrows()
    {
        string path = Write("e.pcap", Classic([(1.0, new byte[100])]));

        Assert.Throws<InvalidDataException>(() => new PcapFrameSource(path));
    }

    [Fact]
    public void WorksWithSequencePlayer()
    {
        var source = new PcapFrameSource(Write("f.pcap", Classic(ThreeRevolutions())));
        var player = new SequencePlayer(source);

        player.Seek(0.15);

        Assert.Equal(1, player.FrameIndex);
    }

    [Fact]
    public void LoadFrameRejectsIndexOutOfRange()
    {
        var source = new PcapFrameSource(Write("g.pcap", Classic(ThreeRevolutions())));

        Assert.Throws<ArgumentOutOfRangeException>(() => source.LoadFrame(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.LoadFrame(-1));
    }
}
