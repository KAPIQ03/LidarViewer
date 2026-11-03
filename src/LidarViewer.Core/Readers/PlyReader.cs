using System.Globalization;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Readers;

/// <summary>
/// Czytnik plików PLY (Stanford Polygon File Format) - ascii oraz binary little/big endian.
///
/// Budowa pliku: nagłówek tekstowy zakończony linią "end_header", potem dane kolejnych
/// elementów (element vertex, element face, ...). Nagłówek opisuje każdy element i jego
/// właściwości (property typ nazwa), więc układ wiersza punktu (które pola, jakich typów)
/// poznajemy dopiero z nagłówka.
///
/// Algorytm:
///  1. Wczytaj nagłówek: format, lista elementów z właściwościami.
///  2. Pomiń elementy stojące przed "vertex" (linie w ascii, bajty w binary).
///  3. Z elementu "vertex" wyciągnij x, y, z (+ opcjonalnie "intensity").
///     Ściany (face) i dalsze elementy są ignorowane - liczą się tylko punkty.
/// </summary>
public static class PlyReader
{
    private sealed record Property(string Name, ScalarType Type, bool IsList);

    private sealed class Element(string name, long count)
    {
        public string Name { get; } = name;
        public long Count { get; } = count;
        public List<Property> Properties { get; } = [];
    }

    public static PointCloud Read(string path)
    {
        using var file = File.OpenRead(path);
        using var s = new BufferedStream(file, 1 << 16);

        if (StreamHelpers.ReadLine(s)?.Trim() != "ply")
            throw new InvalidDataException("To nie jest plik PLY (brak sygnatury 'ply').");

        string format = "";
        var elements = new List<Element>();
        string? line;
        while ((line = StreamHelpers.ReadLine(s)) != null && line.Trim() != "end_header")
        {
            string[] t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            switch (t[0])
            {
                case "format":
                    format = t[1];
                    break;
                case "element":
                    elements.Add(new Element(t[1], long.Parse(t[2], CultureInfo.InvariantCulture)));
                    break;
                case "property":
                    if (elements.Count == 0) throw new InvalidDataException("Właściwość PLY poza elementem.");
                    elements[^1].Properties.Add(t[1] == "list"
                        ? new Property(t[4], ParseType(t[3]), IsList: true)
                        : new Property(t[2], ParseType(t[1]), IsList: false));
                    break;
            }
        }
        if (line == null) throw new InvalidDataException("Nagłówek PLY nie ma linii end_header.");
        if (format is not ("ascii" or "binary_little_endian" or "binary_big_endian"))
            throw new NotSupportedException($"Nieobsługiwany format PLY: '{format}'.");

        foreach (var element in elements)
        {
            if (element.Name == "vertex")
                return ReadVertices(s, element, format);
            SkipElement(s, element, format);
        }
        throw new InvalidDataException("Plik PLY nie zawiera elementu vertex.");
    }

    private static PointCloud ReadVertices(Stream s, Element e, string format)
    {
        if (e.Properties.Any(p => p.IsList))
            throw new NotSupportedException("Właściwości typu list w elemencie vertex nie są obsługiwane.");
        if (e.Count < 0 || e.Count > int.MaxValue / 3)
            throw new InvalidDataException($"Niepoprawna liczba punktów: {e.Count}.");

        int xi = IndexOf(e, "x"), yi = IndexOf(e, "y"), zi = IndexOf(e, "z"), ii = IndexOf(e, "intensity");
        if (xi < 0 || yi < 0 || zi < 0)
            throw new InvalidDataException("Element vertex musi zawierać właściwości x, y i z.");

        var acc = new PointAccumulator(ii >= 0, hasCategory: false, e.Count);

        if (format == "ascii")
        {
            for (long n = 0; n < e.Count; n++)
            {
                string line = StreamHelpers.ReadLine(s) ?? throw new EndOfStreamException("PLY: plik jest obcięty.");
                string[] t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < e.Properties.Count)
                    throw new InvalidDataException($"PLY: wiersz punktu {n + 1} ma za mało wartości.");
                acc.Add(Num(t[xi]), Num(t[yi]), Num(t[zi]), ii >= 0 ? Num(t[ii]) : 0);
            }
        }
        else
        {
            // Układ wiersza binarnego: właściwości jedna po drugiej, bez wyrównania.
            var offsets = new int[e.Properties.Count];
            int stride = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = stride;
                stride += ScalarTypes.SizeOf(e.Properties[i].Type);
            }
            Column Col(int i) => new(offsets[i], e.Properties[i].Type);
            BinaryRows.Read(s, e.Count, stride, format == "binary_big_endian",
                Col(xi), Col(yi), Col(zi), ii >= 0 ? Col(ii) : null, null, acc);
        }
        return acc.ToCloud();
    }

    /// <summary>Pomija dane elementu stojącego przed vertex.</summary>
    private static void SkipElement(Stream s, Element e, string format)
    {
        if (format == "ascii")
        {
            for (long n = 0; n < e.Count; n++)
                if (StreamHelpers.ReadLine(s) == null) throw new EndOfStreamException("PLY: plik jest obcięty.");
            return;
        }
        if (e.Properties.Any(p => p.IsList))
            throw new NotSupportedException($"Element '{e.Name}' z listami przed vertex nie jest obsługiwany.");
        StreamHelpers.Skip(s, e.Count * e.Properties.Sum(p => ScalarTypes.SizeOf(p.Type)));
    }

    private static int IndexOf(Element e, string name) => e.Properties.FindIndex(p => p.Name == name);

    private static double Num(string token) => NumberParser.Parse(token);

    private static ScalarType ParseType(string name) => name switch
    {
        "char" or "int8" => ScalarType.Int8,
        "uchar" or "uint8" => ScalarType.UInt8,
        "short" or "int16" => ScalarType.Int16,
        "ushort" or "uint16" => ScalarType.UInt16,
        "int" or "int32" => ScalarType.Int32,
        "uint" or "uint32" => ScalarType.UInt32,
        "float" or "float32" => ScalarType.Float32,
        "double" or "float64" => ScalarType.Float64,
        _ => throw new NotSupportedException($"Nieobsługiwany typ PLY: '{name}'."),
    };
}
