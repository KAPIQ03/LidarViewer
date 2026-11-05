namespace LidarViewer.Core.Tests;

public abstract class TempDirTestBase : IDisposable
{
    protected string Dir { get; } = Directory.CreateTempSubdirectory("lv-tests").FullName;

    public void Dispose() => Directory.Delete(Dir, recursive: true);

    protected string Write(string name, string content)
    {
        string path = Path.Combine(Dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    protected string Write(string name, byte[] content)
    {
        string path = Path.Combine(Dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
