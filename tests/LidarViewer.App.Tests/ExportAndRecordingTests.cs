using LidarViewer.App.ViewModels;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Readers;

namespace LidarViewer.App.Tests;

public class ExportAndRecordingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lv-app-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PointCloud Cloud(int points = 2) =>
        new([.. Enumerable.Range(0, points).SelectMany(i => new[] { (float)i, 1f, 2f })]);

    [Fact]
    public void ExportCloud_WritesPointsOfDisplayedCloud()
    {
        var vm = new MainViewModel();
        vm.SetSourceForTests(Cloud(3));

        string path = Path.Combine(_dir, "chmura.ply");
        vm.ExportCloud(path);

        Assert.True(File.Exists(path));

        PointCloud read = PointCloudLoader.Read(path);
        Assert.Equal(3, read.Count);
    }

    [Fact]
    public void ExportCloud_ReportsSavedFileInStatus()
    {
        var vm = new MainViewModel();
        vm.SetSourceForTests(Cloud());

        string path = Path.Combine(_dir, "chmura.ply");
        vm.ExportCloud(path);

        Assert.Contains("Zapisano", vm.Status);
    }

    [Fact]
    public void ExportCloud_WithoutCloud_ReportsError()
    {
        var vm = new MainViewModel();

        vm.ExportCloud(Path.Combine(_dir, "chmura.ply"));

        Assert.Contains("Błąd", vm.Status);
    }

    [Fact]
    public void ExportCloud_UnsupportedExtension_ReportsError()
    {
        var vm = new MainViewModel();
        vm.SetSourceForTests(Cloud());

        vm.ExportCloud(Path.Combine(_dir, "chmura.bogus"));

        Assert.Contains("Błąd", vm.Status);
    }

    [Fact]
    public async Task Recording_StopWithoutStart_DoesNothing()
    {
        var vm = new MainViewModel();

        await vm.StopRecordingAsync();

        Assert.False(vm.IsRecording);
        Assert.Equal(0, vm.RecordedPacketCount);
    }
}
