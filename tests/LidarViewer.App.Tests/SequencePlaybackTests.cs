using LidarViewer.App.ViewModels;

namespace LidarViewer.App.Tests;

public class SequencePlaybackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lvapp-" + Guid.NewGuid().ToString("N"));

    public SequencePlaybackTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void WriteSequence(int frames = 3)
    {
        var entries = new List<string>();
        for (int i = 0; i < frames; i++)
        {
            var lines = new List<string>
            {
                "ply", "format ascii 1.0", "element vertex 2",
                "property float x", "property float y", "property float z", "end_header",
                "0 0 0", "1 1 1",
            };
            File.WriteAllLines(Path.Combine(_dir, $"f{i}.ply"), lines);
            entries.Add($"{{\"file\":\"f{i}.ply\",\"timestamp\":{i * 0.1}}}");
        }

        File.WriteAllText(Path.Combine(_dir, "sequence.json"), "{\"frames\":[" + string.Join(",", entries) + "]}");
    }

    [Fact]
    public void LoadingSequenceShowsFirstFrame()
    {
        WriteSequence();
        var vm = new MainViewModel();

        vm.LoadSequenceCommand.Execute(_dir);

        Assert.True(vm.HasSequence);
        Assert.Equal(2, vm.PointCloud?.Count);
        Assert.Equal("Klatka 1 / 3", vm.FrameInfo);
    }

    [Fact]
    public void PlayPauseTogglesPlayback()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);

        vm.PlayPauseCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        vm.PlayPauseCommand.Execute(null);
        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public void AdvanceMovesToNextFrameAndUpdatesCloud()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);
        vm.PlayPauseCommand.Execute(null);

        vm.Advance(TimeSpan.FromMilliseconds(150));

        Assert.Equal(1, vm.Player?.FrameIndex);
        Assert.Equal("Klatka 2 / 3", vm.FrameInfo);
    }

    [Fact]
    public void SeekChangesFrame()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);

        vm.Seek(0.15);

        Assert.Equal(1, vm.Player?.FrameIndex);
        Assert.Equal("Klatka 2 / 3", vm.FrameInfo);
    }

    [Fact]
    public void FailedLoadKeepsApplicationUsable()
    {
        var vm = new MainViewModel();

        vm.LoadSequenceCommand.Execute(_dir);

        Assert.False(vm.HasSequence);
        Assert.Contains("sequence.json", vm.Status);
    }

    [Fact]
    public void LoadingSequenceClearsPipelineStages()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.SetSourceForTests(new Core.Models.PointCloud([0f, 0f, 0f]));
        vm.Pipeline.AddStage.Execute("sphere-crop");

        vm.LoadSequenceCommand.Execute(_dir);

        Assert.Empty(vm.Pipeline.Stages);
    }

    [Fact]
    public void EndOfSequenceStopsPlayback()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);
        vm.PlayPauseCommand.Execute(null);

        bool notified = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPlaying)) notified = true;
        };

        vm.Advance(TimeSpan.FromSeconds(10));

        Assert.False(vm.IsPlaying);
        Assert.True(notified);
        Assert.Equal("Klatka 3 / 3", vm.FrameInfo);
    }

    [Fact]
    public void SpeedSelectionAppliesToPlayer()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);

        vm.Speed = vm.Speeds.First(s => s.Multiplier == 4.0);

        Assert.Equal(4.0, vm.Player?.Speed);
    }

    [Fact]
    public void LoopToggleAppliesToPlayer()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);

        vm.Loop = true;

        Assert.True(vm.Player?.Loop);
    }

    [Fact]
    public async Task LoadingSingleFileClosesPreviousSequence()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);
        vm.PlayPauseCommand.Execute(null);

        string single = Path.Combine(_dir, "f0.ply");
        vm.FilePath = single;
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasSequence);
        Assert.False(vm.IsPlaying);
        Assert.Equal(2, vm.PointCloud?.Count);
    }

    [Fact]
    public void CameraStaysPutBetweenSequenceFramesButRefitsForNewSequence()
    {
        WriteSequence();
        var vm = new MainViewModel();
        int before = vm.FitVersion;

        vm.LoadSequenceCommand.Execute(_dir);
        int afterLoad = vm.FitVersion;
        vm.Seek(0.2);

        Assert.False(vm.AutoFitCamera);
        Assert.True(afterLoad > before);
        Assert.Equal(afterLoad, vm.FitVersion);
    }
}
