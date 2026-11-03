using System.Text;

namespace LidarViewer.Core.Readers;

/// <summary>Pomocnicze odczyty ze strumienia: nagłówki tekstowe przed danymi binarnymi.</summary>
internal static class StreamHelpers
{
    /// <summary>
    /// Czyta jedną linię ASCII (do '\n', bez '\r'), nie wczytując nic poza nią -
    /// dzięki temu zaraz po nagłówku można czytać dane binarne z tego samego strumienia.
    /// Zwraca null na końcu pliku.
    /// </summary>
    public static string? ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        int b;
        bool any = false;
        while ((b = s.ReadByte()) >= 0)
        {
            any = true;
            if (b == '\n') break;
            if (b != '\r') sb.Append((char)b);
            if (sb.Length > 64 * 1024) throw new InvalidDataException("Nagłówek zawiera zbyt długą linię.");
        }
        return any ? sb.ToString() : null;
    }

    /// <summary>Wczytuje dokładnie buffer.Length bajtów lub zgłasza błąd obciętego pliku.</summary>
    public static void ReadExactly(Stream s, Span<byte> buffer, string what)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = s.Read(buffer[total..]);
            if (read == 0) throw new EndOfStreamException($"{what}: plik jest obcięty.");
            total += read;
        }
    }

    /// <summary>Pomija n bajtów strumienia.</summary>
    public static void Skip(Stream s, long n)
    {
        var scratch = new byte[(int)Math.Min(n, 64 * 1024)];
        while (n > 0)
        {
            int chunk = (int)Math.Min(n, scratch.Length);
            ReadExactly(s, scratch.AsSpan(0, chunk), "Pomijane dane");
            n -= chunk;
        }
    }
}
