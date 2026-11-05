using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Vtp;

public static class VtpReader
{
    private const string AppendedMarker = "<AppendedData";
    private const int MaxHeaderBytes = 16 * 1024 * 1024;

    public static PointCloud Read(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var (headerXml, dataStart) = ReadHeader(handle);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(headerXml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException("Niepoprawny nagłówek XML pliku VTP.", ex);
        }

        var root = doc.Root ?? throw new InvalidDataException("Brak elementu głównego VTKFile.");
        ValidateRoot(root);

        int lengthFieldSize = (string?)root.Attribute("header_type") == "UInt64" ? 8 : 4;

        var piece = root.Descendants("Piece").FirstOrDefault()
            ?? throw new InvalidDataException("Brak elementu Piece.");
        int pointCount = int.Parse(
            (string?)piece.Attribute("NumberOfPoints") ?? "0", CultureInfo.InvariantCulture);
        if (pointCount < 0 || pointCount > int.MaxValue / 3)
            throw new InvalidDataException($"Niepoprawna liczba punktów: {pointCount}.");

        var pointsArray = piece.Element("Points")?.Element("DataArray")
            ?? throw new InvalidDataException("Brak tablicy Points.");
        float[] positions = ReadPositions(handle, dataStart, lengthFieldSize, pointsArray, pointCount);

        byte[]? intensity = null;
        int[]? category = null;
        foreach (var array in piece.Element("PointData")?.Elements("DataArray") ?? [])
        {
            string name = (string?)array.Attribute("Name") ?? "";
            string type = (string?)array.Attribute("type") ?? "";
            if (name == "intensity" && type == "UInt8")
                intensity = ReadBlock(handle, dataStart, lengthFieldSize, array, pointCount, sizeof(byte));
            else if (name == "category" && type == "Int32")
                category = ToInt32(ReadBlock(handle, dataStart, lengthFieldSize, array, pointCount, sizeof(int)));
        }

        return new PointCloud(positions, intensity, category);
    }

    private static (string HeaderXml, long DataStart) ReadHeader(SafeFileHandle handle)
    {
        var markerBytes = Encoding.ASCII.GetBytes(AppendedMarker);
        var buffer = new byte[64 * 1024];
        int filled = 0;
        long fileOffset = 0;

        while (true)
        {
            if (filled == buffer.Length)
            {
                if (buffer.Length >= MaxHeaderBytes)
                    throw new InvalidDataException("Nie znaleziono sekcji AppendedData (nagłówek zbyt duży).");
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            int read = RandomAccess.Read(handle, buffer.AsSpan(filled), fileOffset + filled);
            if (read == 0)
                throw new NotSupportedException(
                    "Plik nie zawiera sekcji AppendedData - obsługiwany jest tylko format appended/raw.");
            filled += read;

            int markerPos = buffer.AsSpan(0, filled).IndexOf(markerBytes);
            if (markerPos < 0) continue;

            int underscore = buffer.AsSpan(markerPos, filled - markerPos).IndexOf((byte)'_');
            if (underscore < 0) continue;

            string tag = Encoding.ASCII.GetString(buffer, markerPos, underscore);
            if (!tag.Contains("encoding=\"raw\"", StringComparison.Ordinal))
                throw new NotSupportedException("Obsługiwane jest tylko AppendedData z encoding=\"raw\".");

            string header = Encoding.UTF8.GetString(buffer, 0, markerPos).TrimStart('﻿') + "</VTKFile>";
            return (header, markerPos + underscore + 1);
        }
    }

    private static void ValidateRoot(XElement root)
    {
        if ((string?)root.Attribute("type") != "PolyData")
            throw new NotSupportedException("Obsługiwany jest tylko typ VTK PolyData (.vtp).");
        if ((string?)root.Attribute("byte_order") is { } order && order != "LittleEndian")
            throw new NotSupportedException("Obsługiwana jest tylko kolejność bajtów LittleEndian.");
        if (root.Attribute("compressor") != null)
            throw new NotSupportedException("Skompresowane dane VTP nie są jeszcze obsługiwane.");
    }

    private static float[] ReadPositions(
        SafeFileHandle handle, long dataStart, int lengthFieldSize,
        XElement array, int pointCount)
    {
        string type = (string?)array.Attribute("type") ?? "";
        if (type == "Float32")
        {
            var bytes = ReadBlock(handle, dataStart, lengthFieldSize, array, pointCount * 3, sizeof(float));
            var result = new float[pointCount * 3];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
        if (type == "Float64")
        {
            var bytes = ReadBlock(handle, dataStart, lengthFieldSize, array, pointCount * 3, sizeof(double));
            var result = new float[pointCount * 3];
            for (int i = 0; i < result.Length; i++)
                result[i] = (float)BitConverter.ToDouble(bytes, i * sizeof(double));
            return result;
        }
        throw new NotSupportedException($"Nieobsługiwany typ współrzędnych: {type}.");
    }

    private static byte[] ReadBlock(
        SafeFileHandle handle, long dataStart, int lengthFieldSize,
        XElement array, int elementCount, int elementSize)
    {
        string name = (string?)array.Attribute("Name") ?? "?";
        if ((string?)array.Attribute("format") != "appended")
            throw new NotSupportedException($"Tablica '{name}': obsługiwany jest tylko format=\"appended\".");

        long offset = long.Parse((string?)array.Attribute("offset") ?? "0", CultureInfo.InvariantCulture);
        if (offset < 0)
            throw new InvalidDataException($"Tablica '{name}': ujemny offset.");
        long blockStart = dataStart + offset;

        Span<byte> lengthBytes = stackalloc byte[8];
        lengthBytes = lengthBytes[..lengthFieldSize];
        ReadExactly(handle, lengthBytes, blockStart, name);
        long length = lengthFieldSize == 8
            ? (long)BitConverter.ToUInt64(lengthBytes)
            : BitConverter.ToUInt32(lengthBytes);

        long expected = (long)elementCount * elementSize;
        if (length != expected)
            throw new InvalidDataException(
                $"Tablica '{name}': długość bloku {length} B, oczekiwano {expected} B.");

        var data = new byte[length];
        ReadExactly(handle, data, blockStart + lengthFieldSize, name);
        return data;
    }

    private static void ReadExactly(
        SafeFileHandle handle, Span<byte> buffer, long position, string arrayName)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = RandomAccess.Read(handle, buffer[total..], position + total);
            if (read == 0)
                throw new EndOfStreamException($"Tablica '{arrayName}': plik jest obcięty.");
            total += read;
        }
    }

    private static int[] ToInt32(byte[] bytes)
    {
        var result = new int[bytes.Length / sizeof(int)];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }
}
