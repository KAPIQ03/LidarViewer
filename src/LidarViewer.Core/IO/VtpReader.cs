using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.IO;

public static class VtpReader
{
    public static async Task<IReadOnlyList<LidarPoint>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        await using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, settings);
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        var pieces = document.Descendants().Where(element => element.Name.LocalName == "Piece").ToArray();
        if (pieces.Length == 0)
        {
            throw new InvalidDataException("The VTP file does not contain a PolyData Piece element.");
        }

        var allPoints = new List<LidarPoint>();
        foreach (var piece in pieces)
        {
            allPoints.AddRange(ReadPiece(piece));
        }

        return allPoints;
    }

    private static IReadOnlyList<LidarPoint> ReadPiece(XElement piece)
    {
        var pointsElement = piece.Elements().FirstOrDefault(element => element.Name.LocalName == "Points")
            ?? throw new InvalidDataException("The VTP file does not contain a Points element.");
        var pointsArray = pointsElement.Elements().FirstOrDefault(element => element.Name.LocalName == "DataArray")
            ?? throw new InvalidDataException("The VTP Points element does not contain a DataArray.");

        EnsureAscii(pointsArray, "point coordinates");
        var coordinates = ParseValues(pointsArray.Value, "point coordinates");
        var components = ParsePositiveInt(pointsArray.Attribute("NumberOfComponents")?.Value, 3);
        if (components < 3 || coordinates.Length % components != 0)
        {
            throw new InvalidDataException("The VTP point coordinates must contain complete 3D tuples.");
        }

        var pointCount = coordinates.Length / components;
        var intensities = ReadIntensities(piece, pointCount);
        var points = new LidarPoint[pointCount];
        for (var index = 0; index < pointCount; index++)
        {
            var offset = index * components;
            points[index] = new LidarPoint(
                coordinates[offset],
                coordinates[offset + 1],
                coordinates[offset + 2],
                intensities is null ? 0 : intensities[index]);
        }

        return points;
    }

    private static double[]? ReadIntensities(XElement piece, int pointCount)
    {
        var pointData = piece.Elements().FirstOrDefault(element => element.Name.LocalName == "PointData");
        if (pointData is null)
        {
            return null;
        }

        var arrays = pointData.Elements().Where(element => element.Name.LocalName == "DataArray").ToArray();
        var intensityArray = arrays.FirstOrDefault(element =>
            IsIntensityName(element.Attribute("Name")?.Value));

        intensityArray ??= arrays.FirstOrDefault(element =>
            ParsePositiveInt(element.Attribute("NumberOfComponents")?.Value, 1) == 1);

        if (intensityArray is null)
        {
            return null;
        }

        EnsureAscii(intensityArray, "point intensity");
        var intensities = ParseValues(intensityArray.Value, "point intensity");
        if (intensities.Length != pointCount)
        {
            throw new InvalidDataException("The VTP point intensity count does not match the point count.");
        }

        return intensities;
    }

    private static bool IsIntensityName(string? name) =>
        name is not null &&
        (name.Contains("intensity", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("reflect", StringComparison.OrdinalIgnoreCase));

    private static void EnsureAscii(XElement dataArray, string description)
    {
        var format = dataArray.Attribute("format")?.Value;
        if (format is not null && !string.Equals(format, "ascii", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"This VTP reader supports ASCII DataArray values only; {description} uses '{format}'.");
        }
    }

    private static double[] ParseValues(string value, string description)
    {
        try
        {
            var values = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture))
                .ToArray();
            if (values.Any(number => !double.IsFinite(number)))
            {
                throw new InvalidDataException($"The VTP {description} contains a non-finite number.");
            }

            return values;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The VTP {description} contains an invalid number.", exception);
        }
    }

    private static int ParsePositiveInt(string? value, int defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0)
        {
            return result;
        }

        throw new InvalidDataException($"'{value}' is not a valid positive integer in the VTP file.");
    }
}
