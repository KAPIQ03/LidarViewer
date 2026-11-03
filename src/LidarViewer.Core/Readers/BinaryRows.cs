namespace LidarViewer.Core.Readers;

/// <summary>Położenie jednej wartości (pola) wewnątrz wiersza binarnego.</summary>
internal readonly record struct Column(int Offset, ScalarType Type);

/// <summary>
/// Czyta punkty zapisane jako ciąg równych wierszy binarnych (PLY binary, PCD binary):
/// wczytuje je porcjami (~1 MB) i dla każdego wiersza wyciąga x, y, z oraz opcjonalnie
/// intensywność i kategorię z podanych pól.
/// </summary>
internal static class BinaryRows
{
    public static void Read(
        Stream s, long count, int stride, bool bigEndian,
        Column x, Column y, Column z, Column? intensity, Column? category, PointAccumulator acc)
    {
        if (stride <= 0) throw new InvalidDataException("Nieprawidłowy rozmiar wiersza danych.");
        int rowsPerChunk = Math.Max(1, (1 << 20) / stride);
        var buffer = new byte[rowsPerChunk * stride];

        for (long done = 0; done < count;)
        {
            int rows = (int)Math.Min(rowsPerChunk, count - done);
            StreamHelpers.ReadExactly(s, buffer.AsSpan(0, rows * stride), "Dane punktów");
            for (int r = 0; r < rows; r++)
            {
                var row = buffer.AsSpan(r * stride, stride);
                acc.Add(
                    ScalarTypes.Read(row[x.Offset..], x.Type, bigEndian),
                    ScalarTypes.Read(row[y.Offset..], y.Type, bigEndian),
                    ScalarTypes.Read(row[z.Offset..], z.Type, bigEndian),
                    intensity is { } i ? ScalarTypes.Read(row[i.Offset..], i.Type, bigEndian) : 0,
                    category is { } c ? (int)ScalarTypes.Read(row[c.Offset..], c.Type, bigEndian) : 0);
            }
            done += rows;
        }
    }
}
