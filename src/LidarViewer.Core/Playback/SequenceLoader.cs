using System.Text.Json;

namespace LidarViewer.Core.Playback;

public static class SequenceLoader
{
    public const string ManifestFileName = "sequence.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Sequence Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Katalog sekwencji nie istnieje: {directory}.");

        string manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"W katalogu nie ma pliku {ManifestFileName}.");

        SequenceManifest manifest = ReadManifest(manifestPath);
        return new Sequence(directory, manifest);
    }

    private static SequenceManifest ReadManifest(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            ManifestDto? dto = JsonSerializer.Deserialize<ManifestDto>(json, Options);
            if (dto is null)
                throw new InvalidDataException($"Manifest {ManifestFileName} jest pusty.");

            var frames = dto.Frames
                .Where(f => !string.IsNullOrWhiteSpace(f.File))
                .Select(f => new SequenceFrame(f.File, f.Timestamp))
                .ToArray();

            return new SequenceManifest(dto.Name, frames);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Nie udało się odczytać {ManifestFileName}: {ex.Message}", ex);
        }
    }

    private sealed class ManifestDto
    {
        public string? Name { get; set; }
        public List<FrameDto> Frames { get; set; } = [];
    }

    private sealed class FrameDto
    {
        public string File { get; set; } = "";
        public double? Timestamp { get; set; }
    }
}
