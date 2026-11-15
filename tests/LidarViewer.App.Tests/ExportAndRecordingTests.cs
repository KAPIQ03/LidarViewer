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
    public async Task Recording_SavesLiveFramesAsPlayableSequence()
    {
        var vm = new MainViewModel();
        string target = Path.Combine(_dir, "nagranie");

        vm.StartRecording(target);
        vm.ReceiveLiveCloud(Cloud(3));
        await vm.StopRecordingAsync();

        Assert.False(vm.IsRecording);
        Assert.True(File.Exists(Path.Combine(target, "sequence.json")));

        Sequence sequence = SequenceLoader.Load(target);
        Assert.Single(sequence.Frames);
        Assert.Equal(3, sequence.LoadFrame(0).Count);
    }

    [Fact]
    public async Task Recording_CountsCapturedFrames()
    {
        var vm = new MainViewModel { RecordingIntervalSeconds = 0 };
        vm.StartRecording(Path.Combine(_dir, "nagranie"));

        vm.ReceiveLiveCloud(Cloud());
        vm.ReceiveLiveCloud(Cloud());

        Assert.Equal(2, vm.RecordedFrameCount);
        Assert.Contains("2", vm.RecordInfo);

        // Zakoncz nagranie, zeby watek zapisu nie pisal do katalogu w trakcie jego usuwania.
        await vm.StopRecordingAsync();
    }

    [Fact]
    public async Task Recording_StopWithoutStart_DoesNothing()
    {
        var vm = new MainViewModel();

        await vm.StopRecordingAsync();

        Assert.False(vm.IsRecording);
        Assert.Equal(0, vm.RecordedFrameCount);
    }

    [Fact]
    public void Recording_StartTwice_KeepsSingleRecording()
    {
        var vm = new MainViewModel();
        string target = Path.Combine(_dir, "nagranie");

        vm.StartRecording(target);
        vm.StartRecording(target);

        Assert.True(vm.IsRecording);
    }

    [Fact]
    public async Task Recording_LiveStreamProducesPlayableSequence()
    {
        var vm = new MainViewModel { LivePort = 34812, RecordingIntervalSeconds = 0.2 };
        string target = Path.Combine(_dir, "nagranie");

        vm.SimulatorEnabled = true;
        vm.StartRecording(target);
        await Task.Delay(1500);

        vm.SimulatorEnabled = false;
        vm.StopLiveCommand.Execute(null);
        await vm.StopRecordingAsync();

        Sequence sequence = SequenceLoader.Load(target);

        Assert.NotEmpty(sequence.Frames);
        Assert.True(sequence.LoadFrame(0).Count > 0);
    }
}
