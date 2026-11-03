using LidarViewer.Core.Models;

namespace LidarViewer.Core.Readers;

/// <summary>
/// Czytnik plików tekstowych CSV z punktami.
///
/// Algorytm:
///  1. Pomijamy puste linie i komentarze (zaczynające się od '#').
///  2. Z pierwszej linii danych rozpoznajemy separator: tabulator, ';', ',' albo spacje.
///  3. Jeśli w pierwszej linii któraś z trzech pierwszych komórek nie jest liczbą, uznajemy ją
///     za nagłówek i szukamy kolumn x, y, z oraz (opcjonalnie) intensity/i/reflectivity po nazwie.
///     Bez nagłówka przyjmujemy kolumny 0,1,2 jako XYZ, a kolumnę 3 (jeśli jest) jako intensywność.
///  4. Liczby czytamy niezależnie od ustawień regionalnych (kropka dziesiętna; przy separatorze ';'
///     akceptujemy też przecinek dziesiętny).
/// </summary>
public static class CsvReader
{
    private static readonly string[] IntensityNames = ["intensity", "i", "reflectivity"];

    public static PointCloud Read(string path)
    {
        char? delimiter = null;
        bool firstRow = true;
        int xi = 0, yi = 1, zi = 2, ii = -1;
        PointAccumulator? acc = null;
        int lineNumber = 0;

        foreach (string raw in File.ReadLines(path))
        {
            lineNumber++;
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == '#') continue;

            delimiter ??= DetectDelimiter(line);
            string[] cells = Split(line, delimiter.Value);

            if (firstRow)
            {
                firstRow = false;
                bool hasHeader = cells.Take(3).Any(c => !TryParse(c, delimiter.Value, out _));
                if (hasHeader)
                {
                    (xi, yi, zi, ii) = FindColumns(cells);
                    acc = new PointAccumulator(ii >= 0, hasCategory: false);
                    continue;
                }
                ii = cells.Length > 3 ? 3 : -1;
                acc = new PointAccumulator(ii >= 0, hasCategory: false);
            }

            int needed = Math.Max(Math.Max(xi, yi), Math.Max(zi, ii)) + 1;
            if (cells.Length < needed)
                throw new InvalidDataException($"Linia {lineNumber}: oczekiwano co najmniej {needed} kolumn, jest {cells.Length}.");

            acc!.Add(
                Parse(cells[xi], delimiter.Value, lineNumber),
                Parse(cells[yi], delimiter.Value, lineNumber),
                Parse(cells[zi], delimiter.Value, lineNumber),
                ii >= 0 ? Parse(cells[ii], delimiter.Value, lineNumber) : 0);
        }

        return acc?.ToCloud() ?? new PointCloud([]);
    }

    /// <summary>Szuka kolumn x, y, z i intensywności w wierszu nagłówka (bez względu na wielkość liter).</summary>
    private static (int X, int Y, int Z, int I) FindColumns(string[] header)
    {
        int Find(params string[] names) =>
            Array.FindIndex(header, h => names.Contains(h.Trim('"', '\'', ' ').ToLowerInvariant()));

        int x = Find("x"), y = Find("y"), z = Find("z");
        if (x < 0 || y < 0 || z < 0)
            throw new InvalidDataException("Nagłówek CSV musi zawierać kolumny x, y i z.");
        return (x, y, z, Find(IntensityNames));
    }

    private static char DetectDelimiter(string line) =>
        line.Contains('\t') ? '\t' : line.Contains(';') ? ';' : line.Contains(',') ? ',' : ' ';

    private static string[] Split(string line, char delimiter) =>
        delimiter == ' '
            ? line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) // dowolna liczba białych znaków
            : line.Split(delimiter, StringSplitOptions.TrimEntries);

    private static bool TryParse(string cell, char delimiter, out double value)
    {
        cell = cell.Trim('"');
        if (delimiter == ';') cell = cell.Replace(',', '.'); // przecinek dziesiętny
        return NumberParser.TryParse(cell, out value);
    }

    private static double Parse(string cell, char delimiter, int lineNumber) =>
        TryParse(cell, delimiter, out double v)
            ? v
            : throw new InvalidDataException($"Linia {lineNumber}: '{cell}' nie jest liczbą.");
}
