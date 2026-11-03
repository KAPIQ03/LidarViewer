using System.Buffers.Binary;
using System.Globalization;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Readers;

/// <summary>
/// Czytnik plików PCD (Point Cloud Library) - DATA ascii, binary oraz binary_compressed.
///
/// Nagłówek tekstowy (do linii DATA) zawiera: FIELDS (nazwy pól), SIZE (bajty), TYPE (F/U/I),
/// COUNT (elementów w polu), WIDTH/HEIGHT/POINTS (liczba punktów) i DATA (sposób zapisu).
///
/// Zapis danych:
///  - ascii: jedna linia na punkt, wartości oddzielone spacjami,
///  - binary: punkty jeden po drugim (wiersze równej długości),
///  - binary_compressed: dwie liczby uint32 (rozmiar skompresowany i po dekompresji), potem
///    dane skompresowane LZF. Po rozpakowaniu dane są ułożone kolumnowo: najpierw wszystkie
///    wartości pola 1 dla wszystkich punktów, potem pola 2 itd.
/// </summary>
public static class PcdReader
{
    private sealed record Field(string Name, ScalarType Type, int Size, int Count);

    public static PointCloud Read(string path)
    {
        using var file = File.OpenRead(path);
        using var s = new BufferedStream(file, 1 << 16);

        string[] names = [];
        int[] sizes = [], counts = [];
        char[] types = [];
        long points = -1, width = -1, height = 1;
        string data = "";

        string? line;
        while ((line = StreamHelpers.ReadLine(s)) != null)
        {
            string[] t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0 || t[0].StartsWith('#')) continue;
            switch (t[0].ToUpperInvariant())
            {
                case "FIELDS": names = t[1..]; break;
                case "SIZE": sizes = t[1..].Select(int.Parse).ToArray(); break;
                case "TYPE": types = t[1..].Select(x => x[0]).ToArray(); break;
                case "COUNT": counts = t[1..].Select(int.Parse).ToArray(); break;
                case "WIDTH": width = long.Parse(t[1], CultureInfo.InvariantCulture); break;
                case "HEIGHT": height = long.Parse(t[1], CultureInfo.InvariantCulture); break;
                case "POINTS": points = long.Parse(t[1], CultureInfo.InvariantCulture); break;
                case "DATA": data = t[1].ToLowerInvariant(); break;
            }
            if (data.Length > 0) break; // linia DATA kończy nagłówek
        }
        if (data.Length == 0) throw new InvalidDataException("Nagłówek PCD nie ma linii DATA.");

        if (counts.Length == 0) counts = names.Select(_ => 1).ToArray(); // COUNT jest opcjonalne (domyślnie 1)
        if (names.Length == 0 || sizes.Length != names.Length || types.Length != names.Length || counts.Length != names.Length)
            throw new InvalidDataException("Nagłówek PCD ma niespójne FIELDS/SIZE/TYPE/COUNT.");
        if (points < 0) points = width * height;
        if (points < 0 || points > int.MaxValue / 3)
            throw new InvalidDataException($"Niepoprawna liczba punktów: {points}.");

        var fields = names.Select((n, i) => new Field(n, ParseType(types[i], sizes[i]), sizes[i], counts[i])).ToArray();
        int xi = IndexOf(fields, "x"), yi = IndexOf(fields, "y"), zi = IndexOf(fields, "z"), ii = IndexOf(fields, "intensity");
        if (xi < 0 || yi < 0 || zi < 0)
            throw new InvalidDataException("PCD musi zawierać pola x, y i z.");

        var acc = new PointAccumulator(ii >= 0, hasCategory: false, points);
        switch (data)
        {
            case "ascii":
                ReadAscii(s, fields, points, xi, yi, zi, ii, acc);
                break;
            case "binary":
                ReadBinary(s, fields, points, xi, yi, zi, ii, acc);
                break;
            case "binary_compressed":
                ReadCompressed(s, fields, points, xi, yi, zi, ii, acc);
                break;
            default:
                throw new NotSupportedException($"Nieobsługiwany zapis danych PCD: '{data}'.");
        }
        return acc.ToCloud();
    }

    private static void ReadAscii(Stream s, Field[] fields, long points, int xi, int yi, int zi, int ii, PointAccumulator acc)
    {
        // Indeks kolumny tekstowej pierwszej wartości każdego pola.
        var column = new int[fields.Length];
        for (int i = 1; i < fields.Length; i++) column[i] = column[i - 1] + fields[i - 1].Count;
        int total = column[^1] + fields[^1].Count;

        for (long n = 0; n < points; n++)
        {
            string line = StreamHelpers.ReadLine(s) ?? throw new EndOfStreamException("PCD: plik jest obcięty.");
            string[] t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < total) throw new InvalidDataException($"PCD: wiersz punktu {n + 1} ma za mało wartości.");
            acc.Add(Num(t[column[xi]]), Num(t[column[yi]]), Num(t[column[zi]]), ii >= 0 ? Num(t[column[ii]]) : 0);
        }
    }

    private static void ReadBinary(Stream s, Field[] fields, long points, int xi, int yi, int zi, int ii, PointAccumulator acc)
    {
        var offsets = new int[fields.Length];
        int stride = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            offsets[i] = stride;
            stride += fields[i].Size * fields[i].Count;
        }
        Column Col(int i) => new(offsets[i], fields[i].Type);
        BinaryRows.Read(s, points, stride, bigEndian: false,
            Col(xi), Col(yi), Col(zi), ii >= 0 ? Col(ii) : null, null, acc);
    }

    private static void ReadCompressed(Stream s, Field[] fields, long points, int xi, int yi, int zi, int ii, PointAccumulator acc)
    {
        Span<byte> sizes = stackalloc byte[8];
        StreamHelpers.ReadExactly(s, sizes, "PCD: nagłówek kompresji");
        uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(sizes);
        uint uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(sizes[4..]);

        int stride = fields.Sum(f => f.Size * f.Count);
        if (uncompressed != points * stride)
            throw new InvalidDataException($"PCD: rozmiar po dekompresji ({uncompressed} B) nie zgadza się z liczbą punktów.");

        var packed = new byte[compressed];
        StreamHelpers.ReadExactly(s, packed, "PCD: dane skompresowane");
        byte[] data = Lzf.Decompress(packed, (int)uncompressed);

        // Układ kolumnowy: blok każdego pola ma points * Size * Count bajtów.
        var start = new long[fields.Length];
        for (int i = 1; i < fields.Length; i++)
            start[i] = start[i - 1] + points * fields[i - 1].Size * fields[i - 1].Count;

        double Value(int f, long n) =>
            ScalarTypes.Read(data.AsSpan((int)(start[f] + n * fields[f].Size * fields[f].Count)), fields[f].Type, bigEndian: false);

        for (long n = 0; n < points; n++)
            acc.Add(Value(xi, n), Value(yi, n), Value(zi, n), ii >= 0 ? Value(ii, n) : 0);
    }

    private static int IndexOf(Field[] fields, string name) => Array.FindIndex(fields, f => f.Name == name);

    private static double Num(string token) => NumberParser.Parse(token);

    /// <summary>Mapuje parę TYPE (F/U/I) + SIZE na typ liczbowy.</summary>
    private static ScalarType ParseType(char type, int size) => (char.ToUpperInvariant(type), size) switch
    {
        ('F', 4) => ScalarType.Float32,
        ('F', 8) => ScalarType.Float64,
        ('U', 1) => ScalarType.UInt8,
        ('U', 2) => ScalarType.UInt16,
        ('U', 4) => ScalarType.UInt32,
        ('U', 8) => ScalarType.UInt64,
        ('I', 1) => ScalarType.Int8,
        ('I', 2) => ScalarType.Int16,
        ('I', 4) => ScalarType.Int32,
        ('I', 8) => ScalarType.Int64,
        _ => throw new NotSupportedException($"Nieobsługiwany typ pola PCD: {type}{size}."),
    };
}
