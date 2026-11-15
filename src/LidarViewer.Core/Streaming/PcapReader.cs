using System.Buffers.Binary;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Pakiet UDP wyciagniety z pliku pcap - czas przechwycenia w sekundach
/// (od epoki Uniksa) i dane datagramu bez naglowkow Ethernet/IP/UDP.
/// </summary>
public readonly record struct PcapPacket(double Time, byte[] Payload);

/// <summary>
/// Czytnik plikow przechwytywania sieciowego: klasyczny pcap (mikro- i nanosekundy,
/// oba porzadki bajtow) oraz pcapng. Zwraca tylko niefragmentowane datagramy UDP po IPv4.
///
/// Obslugiwane warstwy lacza: Ethernet (takze VLAN), Linux cooked (SLL),
/// surowy IPv4 i BSD loopback. Pakiety innych typow sa pomijane.
/// W pcapng pomijane sa uproszczone bloki pakietow (SPB) - nie maja znacznika czasu.
/// </summary>
public static class PcapReader
{
    private const int MaxCapturedLength = 1 << 28;

    private const int LinkNull = 0;
    private const int LinkEthernet = 1;
    private const int LinkRaw = 101;
    private const int LinkLinuxSll = 113;
    private const int LinkIpv4 = 228;

    public static IEnumerable<PcapPacket> ReadUdpPackets(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

        byte[] magicBytes = new byte[4];
        if (!TryReadExactly(stream, magicBytes))
            throw new InvalidDataException("Plik przechwytywania jest pusty albo zbyt krótki.");

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(magicBytes);
        IEnumerable<PcapPacket> packets = magic switch
        {
            0xA1B2C3D4 => ReadClassic(stream, bigEndian: false, nanoseconds: false),
            0xA1B23C4D => ReadClassic(stream, bigEndian: false, nanoseconds: true),
            0xD4C3B2A1 => ReadClassic(stream, bigEndian: true, nanoseconds: false),
            0x4D3CB2A1 => ReadClassic(stream, bigEndian: true, nanoseconds: true),
            0x0A0D0D0A => ReadPcapNg(stream),
            _ => throw new InvalidDataException("To nie jest plik pcap ani pcapng."),
        };

        foreach (PcapPacket packet in packets)
            yield return packet;
    }

    private static IEnumerable<PcapPacket> ReadClassic(Stream stream, bool bigEndian, bool nanoseconds)
    {
        byte[] header = new byte[20];
        if (!TryReadExactly(stream, header))
            throw new InvalidDataException("Nagłówek pcap jest ucięty.");

        // Starsze 4 bity pola to rozszerzenia (FCS) - nie wplywaja na typ lacza.
        int linkType = (int)(ReadUInt32(header, 16, bigEndian) & 0x0FFFFFFF);
        double divisor = nanoseconds ? 1e9 : 1e6;

        byte[] recordHeader = new byte[16];
        while (TryReadExactly(stream, recordHeader))
        {
            double time = ReadUInt32(recordHeader, 0, bigEndian) + ReadUInt32(recordHeader, 4, bigEndian) / divisor;
            uint captured = ReadUInt32(recordHeader, 8, bigEndian);
            if (captured > MaxCapturedLength)
                throw new InvalidDataException("Uszkodzony plik pcap: nieprawdopodobna długość pakietu.");

            byte[] data = new byte[captured];
            if (!TryReadExactly(stream, data))
                yield break; // ucieta koncowka przechwytywania

            if (TryExtractUdp(data, linkType, time, out PcapPacket packet))
                yield return packet;
        }
    }

    private static IEnumerable<PcapPacket> ReadPcapNg(Stream stream)
    {
        // Typ bloku (SHB) juz przeczytany; dalej: dlugosc bloku i jego tresc.
        bool bigEndian = false;
        var interfaces = new List<(int LinkType, double Resolution)>();
        byte[] blockHeader = new byte[8];
        bool pendingSectionHeader = true;

        while (true)
        {
            uint type;
            uint totalLength;
            if (pendingSectionHeader)
            {
                pendingSectionHeader = false;
                if (!TryReadExactly(stream, blockHeader.AsSpan(4, 4)))
                    throw new InvalidDataException("Nagłówek pcapng jest ucięty.");
                type = 0x0A0D0D0A;
                totalLength = 0; // dlugosc zalezy od porzadku bajtow - liczona nizej po BOM
            }
            else
            {
                if (!TryReadExactly(stream, blockHeader))
                    yield break;
                type = ReadUInt32(blockHeader, 0, bigEndian);
                totalLength = ReadUInt32(blockHeader, 4, bigEndian);
            }

            if (type == 0x0A0D0D0A)
            {
                // Porzadek bajtow sekcji wynika z "byte-order magic" w tresci bloku.
                byte[] bom = new byte[4];
                if (!TryReadExactly(stream, bom))
                    yield break;

                if (BinaryPrimitives.ReadUInt32LittleEndian(bom) == 0x1A2B3C4D)
                    bigEndian = false;
                else if (BinaryPrimitives.ReadUInt32BigEndian(bom) == 0x1A2B3C4D)
                    bigEndian = true;
                else
                    throw new InvalidDataException("Uszkodzony pcapng: nieznany porządek bajtów.");

                totalLength = ReadUInt32(blockHeader, 4, bigEndian);
                interfaces.Clear();
                if (!SkipBlockRemainder(stream, totalLength, alreadyRead: 12))
                    yield break;
                continue;
            }

            if (totalLength < 12 || totalLength % 4 != 0 || totalLength > MaxCapturedLength)
                throw new InvalidDataException("Uszkodzony plik pcapng: zła długość bloku.");

            byte[] body = new byte[totalLength - 8];
            if (!TryReadExactly(stream, body))
                yield break;

            int bodyLength = body.Length - 4; // ostatnie 4 bajty to powtorzona dlugosc bloku

            if (type == 1 && bodyLength >= 8)
            {
                interfaces.Add((ReadUInt16(body, 0, bigEndian), ReadResolution(body, bodyLength, bigEndian)));
            }
            else if (type == 6 && bodyLength >= 20)
            {
                uint interfaceId = ReadUInt32(body, 0, bigEndian);
                if (interfaceId >= interfaces.Count)
                    continue;

                ulong ticks = ((ulong)ReadUInt32(body, 4, bigEndian) << 32) | ReadUInt32(body, 8, bigEndian);
                uint captured = ReadUInt32(body, 12, bigEndian);
                if (captured > bodyLength - 20)
                    continue;

                var (linkType, resolution) = interfaces[(int)interfaceId];
                byte[] data = body.AsSpan(20, (int)captured).ToArray();
                if (TryExtractUdp(data, linkType, ticks * resolution, out PcapPacket packet))
                    yield return packet;
            }
        }
    }

    /// <summary>
    /// Opcja if_tsresol (kod 9) w bloku opisu interfejsu: bit 7 = potega 2, inaczej potega 10.
    /// Domyslnie mikrosekundy.
    /// </summary>
    private static double ReadResolution(byte[] body, int bodyLength, bool bigEndian)
    {
        int offset = 8;
        while (offset + 4 <= bodyLength)
        {
            int code = ReadUInt16(body, offset, bigEndian);
            int length = ReadUInt16(body, offset + 2, bigEndian);
            if (code == 0)
                break;

            if (code == 9 && length >= 1 && offset + 4 < bodyLength)
            {
                byte value = body[offset + 4];
                return (value & 0x80) != 0 ? Math.Pow(2, -(value & 0x7F)) : Math.Pow(10, -value);
            }

            offset += 4 + ((length + 3) & ~3);
        }

        return 1e-6;
    }

    private static bool SkipBlockRemainder(Stream stream, uint totalLength, int alreadyRead)
    {
        long remaining = (long)totalLength - alreadyRead;
        if (remaining <= 0)
            return true;

        byte[] skip = new byte[remaining];
        return TryReadExactly(stream, skip);
    }

    private static bool TryExtractUdp(byte[] data, int linkType, double time, out PcapPacket packet)
    {
        packet = default;
        int ip;

        switch (linkType)
        {
            case LinkEthernet:
            {
                if (data.Length < 14)
                    return false;

                int etherType = ReadUInt16(data, 12, bigEndian: true);
                ip = 14;
                while (etherType is 0x8100 or 0x88A8)
                {
                    if (data.Length < ip + 4)
                        return false;
                    etherType = ReadUInt16(data, ip + 2, bigEndian: true);
                    ip += 4;
                }

                if (etherType != 0x0800)
                    return false;
                break;
            }
            case LinkLinuxSll:
                if (data.Length < 16 || ReadUInt16(data, 14, bigEndian: true) != 0x0800)
                    return false;
                ip = 16;
                break;
            case LinkRaw:
            case LinkIpv4:
                ip = 0;
                break;
            case LinkNull:
                ip = 4;
                break;
            default:
                return false;
        }

        if (data.Length < ip + 20 || data[ip] >> 4 != 4)
            return false;

        int headerLength = (data[ip] & 0x0F) * 4;
        if (headerLength < 20 || data[ip + 9] != 17)
            return false;

        // Fragmenty IP (flaga MF albo przesuniecie) nie niosa kompletnego datagramu UDP.
        if ((ReadUInt16(data, ip + 6, bigEndian: true) & 0x3FFF) != 0)
            return false;

        int udp = ip + headerLength;
        if (data.Length < udp + 8)
            return false;

        int payloadLength = Math.Min(ReadUInt16(data, udp + 4, bigEndian: true) - 8, data.Length - udp - 8);
        if (payloadLength <= 0)
            return false;

        packet = new PcapPacket(time, data.AsSpan(udp + 8, payloadLength).ToArray());
        return true;
    }

    private static uint ReadUInt32(byte[] buffer, int offset, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));

    private static ushort ReadUInt16(byte[] buffer, int offset, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset));

    /// <summary>
    /// Czyta dokladnie tyle bajtow, ile zmiesci bufor. False, gdy strumien
    /// skonczyl sie wczesniej (takze przy zerowej liczbie bajtow).
    /// </summary>
    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
                return false;
            total += read;
        }

        return true;
    }
}
