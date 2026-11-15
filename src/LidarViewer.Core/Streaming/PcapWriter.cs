using System.Buffers.Binary;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Zapisuje datagramy UDP do klasycznego pliku pcap (LINKTYPE_ETHERNET, mikrosekundy, little endian),
/// ktory otwieraja Wireshark, VeloView i <see cref="PcapReader"/>. Kazdy ladunek dostaje syntetyczne
/// naglowki Ethernet/IPv4/UDP z poprawna suma kontrolna IP - tak samo robi VeloView przy nagrywaniu.
/// Zapis jest bezpieczny watkowo.
/// </summary>
public sealed class PcapWriter : IDisposable
{
    private const int EthernetHeader = 14;
    private const int IpHeader = 20;
    private const int UdpHeader = 8;
    private const int MaxPayload = 65535 - IpHeader - UdpHeader;

    private static readonly byte[] SourceAddress = [192, 168, 1, 201];       // typowy adres czujnika Velodyne
    private static readonly byte[] DestinationAddress = [255, 255, 255, 255]; // rozgloszenie

    private readonly FileStream _stream;
    private readonly object _gate = new();
    private readonly ushort _sourcePort;
    private readonly ushort _destinationPort;
    private long _packetCount;
    private bool _disposed;

    public PcapWriter(string path, ushort destinationPort = 2368, ushort sourcePort = 2368)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _destinationPort = destinationPort;
        _sourcePort = sourcePort;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);

        Span<byte> header = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0xA1B2C3D4);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 2);   // wersja 2.4
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 65535); // snaplen
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], 1);     // Ethernet
        _stream.Write(header);
    }

    public long PacketCount => Interlocked.Read(ref _packetCount);

    /// <summary>
    /// Dopisuje datagram. Czas to chwila odbioru (UTC).
    /// </summary>
    public void Write(ReadOnlySpan<byte> payload, DateTimeOffset time)
    {
        if (payload.Length > MaxPayload)
            throw new ArgumentException("Datagram jest za duży na pakiet IPv4.", nameof(payload));

        int length = EthernetHeader + IpHeader + UdpHeader + payload.Length;
        byte[] record = new byte[16 + length];

        long micros = time.ToUnixTimeMilliseconds() * 1000 + time.Ticks % TimeSpan.TicksPerMillisecond / 10;
        BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)(micros / 1_000_000));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)(micros % 1_000_000));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), (uint)length);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), (uint)length);

        Span<byte> frame = record.AsSpan(16);
        frame[..6].Fill(0xFF);                                  // MAC docelowy: rozgloszenie
        frame[6..12].Clear();                                   // MAC zrodlowy: zera
        BinaryPrimitives.WriteUInt16BigEndian(frame[12..], 0x0800);

        Span<byte> ip = frame[EthernetHeader..];
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip[2..], (ushort)(IpHeader + UdpHeader + payload.Length));
        BinaryPrimitives.WriteUInt16BigEndian(ip[6..], 0x4000); // Don't Fragment
        ip[8] = 64;                                             // TTL
        ip[9] = 17;                                             // UDP
        SourceAddress.CopyTo(ip[12..]);
        DestinationAddress.CopyTo(ip[16..]);
        BinaryPrimitives.WriteUInt16BigEndian(ip[10..], Checksum(ip[..IpHeader]));

        Span<byte> udp = ip[IpHeader..];
        BinaryPrimitives.WriteUInt16BigEndian(udp, _sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[2..], _destinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], (ushort)(UdpHeader + payload.Length));
        payload.CopyTo(udp[UdpHeader..]);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _stream.Write(record);
            _packetCount++;
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_disposed)
                _stream.Flush();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _stream.Dispose();
        }
    }

    private static ushort Checksum(ReadOnlySpan<byte> header)
    {
        uint sum = 0;
        for (int i = 0; i < header.Length; i += 2)
            sum += BinaryPrimitives.ReadUInt16BigEndian(header[i..]);

        while (sum >> 16 != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        return (ushort)~sum;
    }
}
