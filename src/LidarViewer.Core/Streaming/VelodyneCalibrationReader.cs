using System.Globalization;
using System.Xml.Linq;

namespace LidarViewer.Core.Streaming;

/// <summary>
/// Czyta plik kalibracji Velodyne w formacie db.xml (boost_serialization), takim jak w VeloView.
/// Uwzgledniane pola: rotCorrection_, vertCorrection_ (stopnie), distCorrection_,
/// vertOffsetCorrection_, horizOffsetCorrection_ (centymetry). Pozostale korekty (ogniskowa,
/// dwupunktowa korekta dystansu HDL-64E, intensywnosc) sa pomijane.
/// </summary>
public static class VelodyneCalibrationReader
{
    public static VelodyneCalibration Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException($"Plik kalibracji nie jest poprawnym XML: {ex.Message}", ex);
        }

        var points = document.Descendants("points_").FirstOrDefault()
            ?? throw new InvalidDataException("W pliku kalibracji brak sekcji points_.");

        var lasers = new SortedDictionary<int, LaserCalibration>();
        foreach (XElement px in points.Descendants("px"))
        {
            int id = (int)ReadNumber(px, "id_", required: true);
            lasers[id] = new LaserCalibration(
                RotationCorrection: (float)ReadNumber(px, "rotCorrection_"),
                VerticalAngle: (float)ReadNumber(px, "vertCorrection_"),
                DistanceCorrection: (float)(ReadNumber(px, "distCorrection_") / 100.0),
                VerticalOffset: (float)(ReadNumber(px, "vertOffsetCorrection_") / 100.0),
                HorizontalOffset: (float)(ReadNumber(px, "horizOffsetCorrection_") / 100.0));
        }

        if (lasers.Count == 0)
            throw new InvalidDataException("W pliku kalibracji nie znaleziono żadnego lasera.");

        // Numery laserow musza byc ciagle od 0, bo decoder indeksuje nimi pakiet.
        for (int i = 0; i < lasers.Count; i++)
            if (!lasers.ContainsKey(i))
                throw new InvalidDataException($"W kalibracji brakuje lasera o numerze {i}.");

        return new VelodyneCalibration(Path.GetFileNameWithoutExtension(path), lasers.Values.ToArray());
    }

    private static double ReadNumber(XElement parent, string name, bool required = false)
    {
        string? text = parent.Element(name)?.Value;
        if (text is null)
        {
            if (required)
                throw new InvalidDataException($"W kalibracji brakuje pola {name}.");
            return 0;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new InvalidDataException($"Pole {name} ma nieprawidłową wartość '{text}'.");

        return value;
    }
}
