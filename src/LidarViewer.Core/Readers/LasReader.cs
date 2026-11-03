using System.Buffers.Binary;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Readers;

/// <summary>
/// Czytnik plików LAS (ASPRS LAS 1.0-1.4, formaty punktów 0-10). Skompresowane LAZ nie są obsługiwane.
///
/// Budowa pliku: stały nagłówek binarny (little-endian), opcjonalne rekordy VLR, potem tablica
/// rekordów punktów o jednakowej długości. Położenia pól w nagłówku (bajty):
///   94 rozmiar nagłówka, 96 offset danych punktów, 104 format rekordu, 105 długość rekordu,
///   107 liczba punktów (1.4: uint64 pod 247), 131/139/147 skala XYZ, 155/163/171 przesunięcie XYZ.
/// Każdy rekord zaczyna się od X, Y, Z jako int32; rzeczywista współrzędna = wartość * skala + przesunięcie.
/// Dalej: intensywność (uint16, bajt 12) i klasyfikacja (bajt 15 dla formatów 0-5, bajt 16 dla 6-10).
///
/// Współrzędne LAS bywają ogromne (układy państwowe), a float ma tylko ~7 cyfr, więc od wszystkich
/// punktów odejmujemy minimum (PointCloud.Origin) - pozycje float są względem niego.
/// </summary>
public static class LasReader
{
    // Minimalna długość rekordu dla formatów 0-10.
    private static readonly int[] MinRecordLength = [20, 28, 26, 34, 57, 63, 30, 36, 38, 59, 67];

    public static PointCloud Read(string path)
    {
        using var file = File.OpenRead(path);

        // --- nagłówek ---
        var header = new byte[375];
        int headerRead = file.Read(header, 0, header.Length);
        if (headerRead < 227 || header[0] != 'L' || header[1] != 'A' || header[2] != 'S' || header[3] != 'F')
            throw new InvalidDataException("To nie jest plik LAS (brak sygnatury 'LASF').");
        var h = header.AsSpan(0, headerRead);

        int versionMinor = h[25];
        uint pointDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(h[96..]);
        byte rawFormat = h[104];
        if ((rawFormat & 0xC0) != 0)
            throw new NotSupportedException("Skompresowane pliki LAZ nie są obsługiwane - użyj nieskompresowanego LAS.");
        int format = rawFormat & 0x3F;
        if (format >= MinRecordLength.Length)
            throw new NotSupportedException($"Nieobsługiwany format rekordu punktów LAS: {format}.");
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(h[105..]);
        if (recordLength < MinRecordLength[format])
            throw new InvalidDataException($"Za krótki rekord punktu ({recordLength} B) dla formatu {format}.");

        long count = BinaryPrimitives.ReadUInt32LittleEndian(h[107..]);
        if (count == 0 && versionMinor >= 4 && headerRead >= 375)
            count = (long)BinaryPrimitives.ReadUInt64LittleEndian(h[247..]); // LAS 1.4: 64-bitowa liczba punktów
        if (count > int.MaxValue / 3)
            throw new InvalidDataException($"Za duża liczba punktów: {count}.");

        double scaleX = BinaryPrimitives.ReadDoubleLittleEndian(h[131..]);
        double scaleY = BinaryPrimitives.ReadDoubleLittleEndian(h[139..]);
        double scaleZ = BinaryPrimitives.ReadDoubleLittleEndian(h[147..]);
        double offsetX = BinaryPrimitives.ReadDoubleLittleEndian(h[155..]);
        double offsetY = BinaryPrimitives.ReadDoubleLittleEndian(h[163..]);
        double offsetZ = BinaryPrimitives.ReadDoubleLittleEndian(h[171..]);

        // --- rekordy punktów, czytane porcjami ---
        int n = (int)count;
        var rawX = new int[n];
        var rawY = new int[n];
        var rawZ = new int[n];
        var intensity = new ushort[n];
        var category = new int[n];
        int classOffset = format <= 5 ? 15 : 16;
        int classMask = format <= 5 ? 0x1F : 0xFF; // w formatach 0-5 górne 3 bity to flagi

        file.Seek(pointDataOffset, SeekOrigin.Begin);
        int rowsPerChunk = Math.Max(1, (1 << 20) / recordLength);
        var buffer = new byte[rowsPerChunk * recordLength];
        for (int done = 0; done < n;)
        {
            int rows = Math.Min(rowsPerChunk, n - done);
            StreamHelpers.ReadExactly(file, buffer.AsSpan(0, rows * recordLength), "LAS: rekordy punktów");
            for (int r = 0; r < rows; r++)
            {
                var rec = buffer.AsSpan(r * recordLength, recordLength);
                int i = done + r;
                rawX[i] = BinaryPrimitives.ReadInt32LittleEndian(rec);
                rawY[i] = BinaryPrimitives.ReadInt32LittleEndian(rec[4..]);
                rawZ[i] = BinaryPrimitives.ReadInt32LittleEndian(rec[8..]);
                intensity[i] = BinaryPrimitives.ReadUInt16LittleEndian(rec[12..]);
                category[i] = rec[classOffset] & classMask;
            }
            done += rows;
        }

        if (n == 0) return new PointCloud([]);

        // --- przeliczenie na float względem minimum (Origin) ---
        int minX = rawX.Min(), minY = rawY.Min(), minZ = rawZ.Min();
        var positions = new float[n * 3];
        for (int i = 0; i < n; i++)
        {
            // Różnicę liczymy na long (bez przepełnienia int), a mnożenie przez skalę na double.
            positions[i * 3] = (float)(((long)rawX[i] - minX) * scaleX);
            positions[i * 3 + 1] = (float)(((long)rawY[i] - minY) * scaleY);
            positions[i * 3 + 2] = (float)(((long)rawZ[i] - minZ) * scaleZ);
        }

        // Intensywność 16-bitowa -> bajt: jeśli wszystkie wartości mieszczą się w 0-255, zostawiamy je,
        // w przeciwnym razie skalujemy (>> 8).
        int shift = intensity.Max() <= 255 ? 0 : 8;
        var intensityBytes = new byte[n];
        for (int i = 0; i < n; i++) intensityBytes[i] = (byte)(intensity[i] >> shift);

        return new PointCloud(positions, intensityBytes, category)
        {
            Origin = (minX * scaleX + offsetX, minY * scaleY + offsetY, minZ * scaleZ + offsetZ),
        };
    }
}
