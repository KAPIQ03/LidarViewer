using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Streaming;

namespace LidarViewer.Core.Tests.Streaming;

public class PcapWriterTests : TempDirTestBase
{
    private static byte[] VelodynePacket(ushort azimuth)
    {
        byte[] packet = new byte[VelodyneDecoder.PacketSize];
        for (int block = 0; block < VelodyneDecoder.BlocksPerPacket; block++)
        {
            int offset = block * VelodyneDecoder.BlockSize;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset, 2), VelodyneDecoder.UpperBank);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset + 2, 2), azimuth);
            for (int scan = 0; scan < 32; scan++)
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset + 4 + scan * 3, 2), 1000);
        }
        return packet;
    }

    [Fact]
    public void WrittenPacketsReadBackWithPayloadAndTime()
    {
        string path = Path.Combine(Dir, "a.pcap");
        var time = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1234560); // 123456 us

        using (var writer = new PcapWriter(path))
        {
            writer.Write([1, 2, 3, 4], time);
            writer.Write(new byte[1206], time.AddSeconds(1));
            Assert.Equal(2, writer.PacketCount);
        }

        var packets = PcapReader.ReadUdpPackets(path).ToList();

        Assert.Equal(2, packets.Count);
        Assert.Equal([1, 2, 3, 4], packets[0].Payload);
        Assert.Equal(1206, packets[1].Payload.Length);
        Assert.Equal(time.ToUnixTimeMilliseconds() / 1000.0 + 0.000456, packets[0].Time, 5);
        Assert.Equal(1.0, packets[1].Time - packets[0].Time, 5);
    }

    [Fact]
    public void FileHasPcapHeaderAndValidIpChecksum()
    {
        string path = Path.Combine(Dir, "b.pcap");
        using (var writer = new PcapWriter(path, destinationPort: 2368))
            writer.Write(new byte[100], DateTimeOffset.UtcNow);

        byte[] bytes = File.ReadAllBytes(path);

        Assert.Equal(0xA1B2C3D4u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20))); // Ethernet

        int ip = 24 + 16 + 14;
        Assert.Equal(0x45, bytes[ip]);
        Assert.Equal(17, bytes[ip + 9]);

        // Suma 16-bitowych slow naglowka IP razem z suma kontrolna musi dac 0xFFFF.
        uint sum = 0;
        for (int i = 0; i < 20; i += 2)
            sum += BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(ip + i));
        while (sum >> 16 != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        Assert.Equal(0xFFFFu, sum);

        Assert.Equal(2368, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(ip + 20 + 2)));
    }

    [Fact]
    public void RecordedVelodyneStreamPlaysBackAsFrames()
    {
        string path = Path.Combine(Dir, "c.pcap");
        var start = DateTimeOffset.UtcNow;
        using (var writer = new PcapWriter(path))
        {
            ushort[] azimuths = [0, 1800, 0, 1800];
            for (int i = 0; i < azimuths.Length; i++)
                writer.Write(VelodynePacket(azimuths[i]), start.AddMilliseconds(i * 50));
        }

        var source = new PcapFrameSource(path);

        Assert.Equal(2, source.FrameCount);
        Assert.Equal(2 * 12 * 32, source.LoadFrame(0).Count);
        Assert.Equal(0.1, source.Times[1], 3);
    }

    [Fact]
    public void OversizedPayloadIsRejected()
    {
        using var writer = new PcapWriter(Path.Combine(Dir, "d.pcap"));

        Assert.Throws<ArgumentException>(() => writer.Write(new byte[70000], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void WriteAfterDisposeThrows()
    {
        var writer = new PcapWriter(Path.Combine(Dir, "e.pcap"));
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.Write([1], DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ReceiverRaisesRawPacketEventForEveryDatagram()
    {
        using var receiver = new UdpCloudReceiver(0, new SimpleCloudDecoder(), 100);
        var received = new List<byte[]>();
        receiver.PacketReceived += (payload, _) =>
        {
            lock (received)
                received.Add(payload.ToArray());
        };
        receiver.Start();

        using var sender = new UdpClient();
        await sender.SendAsync(new byte[] { 9, 9, 9 }, new IPEndPoint(IPAddress.Loopback, receiver.Port));

        for (int i = 0; i < 50 && received.Count == 0; i++)
            await Task.Delay(100);

        lock (received)
        {
            // Nierozpoznany datagram tez trafia do nagrania - sluzy do zapisu pcap.
            Assert.Single(received);
            Assert.Equal([9, 9, 9], received[0]);
        }
    }
}
