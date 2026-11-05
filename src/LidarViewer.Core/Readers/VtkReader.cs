using System.Globalization;
using System.Text;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Readers;

public static class VtkReader
{
    public static PointCloud Read(string path)
    {
        var sc = new ByteScanner(File.ReadAllBytes(path));

        if (sc.ReadLine()?.StartsWith("# vtk DataFile", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidDataException("To nie jest plik VTK (brak nagłówka '# vtk DataFile').");
        sc.ReadLine();

        string mode = sc.ReadToken()?.ToUpperInvariant() ?? "";
        if (mode is not ("ASCII" or "BINARY"))
            throw new NotSupportedException($"Nieobsługiwany tryb zapisu VTK: '{mode}'.");
        bool binary = mode == "BINARY";

        string? token;
        while ((token = sc.ReadToken()) != null && !token.Equals("POINTS", StringComparison.OrdinalIgnoreCase)) { }
        if (token == null)
            throw new NotSupportedException("Plik VTK nie zawiera sekcji POINTS (obsługiwane są zbiory z punktami).");

        int count = ParseInt(sc.ReadToken());
        if (count < 0 || count > int.MaxValue / 3) throw new InvalidDataException($"Niepoprawna liczba punktów: {count}.");
        var pointType = ParseType(sc.ReadToken());
        BeginBinary(sc, binary);
        double[] xyz = ReadValues(sc, binary, pointType, (long)count * 3);

        var positions = new float[count * 3];
        for (int i = 0; i < positions.Length; i++) positions[i] = (float)xyz[i];

        byte[]? intensity = null;
        int[]? category = null;
        bool inPointData = false;
        long attributeCount = count;
        while ((token = sc.ReadToken()) != null)
        {
            switch (token.ToUpperInvariant())
            {
                case "VERTICES" or "LINES" or "POLYGONS" or "TRIANGLE_STRIPS" or "CELLS":
                    sc.ReadToken();
                    long size = ParseInt(sc.ReadToken());
                    BeginBinary(sc, binary);
                    SkipValues(sc, binary, ScalarType.Int32, size);
                    break;
                case "CELL_TYPES":
                    long cells = ParseInt(sc.ReadToken());
                    BeginBinary(sc, binary);
                    SkipValues(sc, binary, ScalarType.Int32, cells);
                    break;
                case "CELL_DATA":
                    attributeCount = ParseInt(sc.ReadToken());
                    inPointData = false;
                    break;
                case "POINT_DATA":
                    attributeCount = ParseInt(sc.ReadToken());
                    inPointData = true;
                    break;
                case "SCALARS":
                    {
                        string name = sc.ReadToken() ?? "";
                        var type = ParseType(sc.ReadToken());
                        int components = 1;
                        if (int.TryParse(sc.PeekToken(), out int nc)) { components = nc; sc.ReadToken(); }
                        if (!"LOOKUP_TABLE".Equals(sc.ReadToken(), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("VTK: po SCALARS oczekiwano LOOKUP_TABLE.");
                        sc.ReadToken();
                        BeginBinary(sc, binary);
                        CollectOrSkip(sc, binary, type, attributeCount * components, inPointData ? name : "", components,
                            ref intensity, ref category);
                        break;
                    }
                default:
                    return Build(positions, intensity, category);
            }
        }
        return Build(positions, intensity, category);
    }

    private static PointCloud Build(float[] positions, byte[]? intensity, int[]? category) =>
        new(positions, intensity, category);

    private static void CollectOrSkip(
        ByteScanner sc, bool binary, ScalarType type, long total, string name, int components,
        ref byte[]? intensity, ref int[]? category)
    {
        bool wantIntensity = name.Equals("intensity", StringComparison.OrdinalIgnoreCase);
        bool wantCategory = name.Equals("category", StringComparison.OrdinalIgnoreCase);
        if (!wantIntensity && !wantCategory)
        {
            SkipValues(sc, binary, type, total);
            return;
        }
        double[] values = ReadValues(sc, binary, type, total);
        int points = (int)(total / components);
        if (wantIntensity)
        {
            intensity = new byte[points];
            for (int i = 0; i < points; i++) intensity[i] = ScalarTypes.ToByte(values[i * components]);
        }
        else
        {
            category = new int[points];
            for (int i = 0; i < points; i++) category[i] = (int)values[i * components];
        }
    }

    private static void BeginBinary(ByteScanner sc, bool binary)
    {
        if (binary) sc.SkipLine();
    }

    private static double[] ReadValues(ByteScanner sc, bool binary, ScalarType type, long count)
    {
        var values = new double[count];
        if (binary)
        {
            int size = ScalarTypes.SizeOf(type);
            var bytes = sc.ReadBytes(count * size);
            for (long i = 0; i < count; i++)
                values[i] = ScalarTypes.Read(bytes[(int)(i * size)..], type, bigEndian: true);
        }
        else
        {
            for (long i = 0; i < count; i++)
                values[i] = NumberParser.Parse(sc.ReadToken() ?? throw new EndOfStreamException("VTK: plik jest obcięty."));
        }
        return values;
    }

    private static void SkipValues(ByteScanner sc, bool binary, ScalarType type, long count)
    {
        if (binary)
            sc.ReadBytes(count * ScalarTypes.SizeOf(type));
        else
            for (long i = 0; i < count; i++)
                if (sc.ReadToken() == null) throw new EndOfStreamException("VTK: plik jest obcięty.");
    }

    private static int ParseInt(string? token) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : throw new InvalidDataException($"VTK: oczekiwano liczby całkowitej, jest '{token}'.");

    private static ScalarType ParseType(string? name) => name?.ToLowerInvariant() switch
    {
        "char" => ScalarType.Int8,
        "unsigned_char" => ScalarType.UInt8,
        "short" => ScalarType.Int16,
        "unsigned_short" => ScalarType.UInt16,
        "int" => ScalarType.Int32,
        "unsigned_int" => ScalarType.UInt32,
        "long" or "vtktypeint64" => ScalarType.Int64,
        "unsigned_long" or "vtktypeuint64" => ScalarType.UInt64,
        "float" => ScalarType.Float32,
        "double" => ScalarType.Float64,
        _ => throw new NotSupportedException($"Nieobsługiwany typ danych VTK: '{name}'."),
    };

    private sealed class ByteScanner(byte[] data)
    {
        private int _pos;

        private static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

        public string? ReadToken()
        {
            while (_pos < data.Length && IsSpace(data[_pos])) _pos++;
            if (_pos >= data.Length) return null;
            int start = _pos;
            while (_pos < data.Length && !IsSpace(data[_pos])) _pos++;
            return Encoding.ASCII.GetString(data, start, _pos - start);
        }

        public string? PeekToken()
        {
            int saved = _pos;
            string? token = ReadToken();
            _pos = saved;
            return token;
        }

        public string? ReadLine()
        {
            if (_pos >= data.Length) return null;
            int start = _pos;
            while (_pos < data.Length && data[_pos] != '\n') _pos++;
            string line = Encoding.ASCII.GetString(data, start, _pos - start).TrimEnd('\r');
            if (_pos < data.Length) _pos++;
            return line;
        }

        public void SkipLine() => ReadLine();

        public ReadOnlySpan<byte> ReadBytes(long length)
        {
            if (length < 0 || _pos + length > data.Length)
                throw new EndOfStreamException("VTK: plik jest obcięty.");
            var span = data.AsSpan(_pos, (int)length);
            _pos += (int)length;
            return span;
        }
    }
}
