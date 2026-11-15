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

    [Fact]
    public void ChangingPositionBySliderSeeksSequence()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);

        vm.PositionSeconds = 0.2;

        Assert.Equal(0.2, vm.Player!.PositionSeconds, 6);
        Assert.Equal("Klatka 3 / 3", vm.FrameInfo);
    }

    [Fact]
    public void PlayerAdvanceDoesNotTriggerExtraSeek()
    {
        WriteSequence();
        var vm = new MainViewModel();
        vm.LoadSequenceCommand.Execute(_dir);
        vm.PlayPauseCommand.Execute(null);

        vm.Advance(TimeSpan.FromSeconds(0.15));

        Assert.Equal(0.15, vm.PositionSeconds, 6);
        Assert.Equal("Klatka 2 / 3", vm.FrameInfo);
    }

    private static byte[] VelodynePcap(params (double Time, ushort Azimuth)[] packets)
    {
        using var ms = new MemoryStream();
        void U32(uint v)
        {
            byte[] b = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            ms.Write(b);
        }

        U32(0xA1B2C3D4u); U32(0x00040002); U32(0); U32(0); U32(65535); U32(1);
        foreach (var (time, azimuth) in packets)
        {
            byte[] payload = new byte[1206];
            for (int block = 0; block < 12; block++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(block * 100, 2), 0xEEFF);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(block * 100 + 2, 2), azimuth);
                for (int scan = 0; scan < 32; scan++)
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(block * 100 + 4 + scan * 3, 2), 1000);
            }

            byte[] frame = new byte[14 + 20 + 8 + payload.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), 0x0800);
            frame[14] = 0x45;
            frame[14 + 9] = 17;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(14 + 20 + 4), (ushort)(8 + payload.Length));
            payload.CopyTo(frame, 14 + 20 + 8);

            uint sec = (uint)Math.Floor(time);
            U32(sec); U32((uint)Math.Round((time - sec) * 1e6)); U32((uint)frame.Length); U32((uint)frame.Length);
            ms.Write(frame);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task OpeningPcapPlaysFramesWithoutTemporaryFiles()
    {
        string pcap = Path.Combine(_dir, "nagranie.pcap");
        File.WriteAllBytes(pcap, VelodynePcap((50.0, 0), (50.05, 1800), (50.1, 0), (50.15, 1800)));
        var vm = new MainViewModel { FilePath = pcap };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasSequence);
        Assert.IsType<LidarViewer.Core.Playback.PcapFrameSource>(vm.Sequence);
        Assert.Equal("Klatka 1 / 2", vm.FrameInfo);
        Assert.Equal(2 * 12 * 32, vm.PointCloud?.Count);

        vm.PositionSeconds = 0.12; // druga klatka zaczyna sie po ok. 0.1 s (znaczniki czasu w pliku sa zaokraglone do mikrosekund)
        Assert.Equal("Klatka 2 / 2", vm.FrameInfo);
    }

    [Fact]
    public async Task PcapWithoutVelodynePacketsReportsError()
    {
        string pcap = Path.Combine(_dir, "pusty.pcap");
        File.WriteAllBytes(pcap, VelodynePcap());
        var vm = new MainViewModel { FilePath = pcap };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasSequence);
        Assert.StartsWith("Błąd", vm.Status);
    }
}
