using System.Globalization;
using System.Text;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Recording;

public sealed class CsvPointCloudRecorder : IAsyncDisposable
{
    private readonly StreamWriter _writer;

    private CsvPointCloudRecorder(StreamWriter writer)
    {
        _writer = writer;
    }

    public static async Task<CsvPointCloudRecorder> CreateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            await writer.WriteLineAsync("timestamp_utc,x,y,z,intensity".AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return new CsvPointCloudRecorder(writer);
        }
        catch
        {
            await writer.DisposeAsync();
            throw;
        }
    }

    public async Task AppendAsync(
        IReadOnlyList<LidarPoint> points,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        var timestampText = timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        foreach (var point in points)
        {
            await _writer.WriteLineAsync(string.Join(",",
                timestampText,
                point.X.ToString("R", CultureInfo.InvariantCulture),
                point.Y.ToString("R", CultureInfo.InvariantCulture),
                point.Z.ToString("R", CultureInfo.InvariantCulture),
                point.Intensity.ToString("R", CultureInfo.InvariantCulture)).AsMemory(), cancellationToken);
        }

        await _writer.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }
}
