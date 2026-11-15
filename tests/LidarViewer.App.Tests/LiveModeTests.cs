using LidarViewer.App.ViewModels;
using LidarViewer.Core.Models;

namespace LidarViewer.App.Tests;

public class LiveModeTests
{
    [Fact]
    public void StartLiveCommandStartsReceiver()
    {
        var vm = new MainViewModel { LivePort = 34567 };

        vm.StartLiveCommand.Execute(null);

        Assert.True(vm.IsLive);
    }

    [Fact]
    public void StopLiveCommandStopsReceiver()
    {
        var vm = new MainViewModel { LivePort = 34568 };
        vm.StartLiveCommand.Execute(null);

        vm.StopLiveCommand.Execute(null);

        Assert.False(vm.IsLive);
    }

    [Fact]
    public void StartLiveTwiceKeepsSingleReceiver()
    {
        var vm = new MainViewModel { LivePort = 34569 };

        vm.StartLiveCommand.Execute(null);
        vm.StartLiveCommand.Execute(null);

        Assert.True(vm.IsLive);
        vm.StopLiveCommand.Execute(null);
    }

    [Fact]
    public void InvalidPortReportsErrorInsteadOfCrashing()
    {
        var vm = new MainViewModel { LivePort = 70000 };

        vm.StartLiveCommand.Execute(null);

        Assert.False(vm.IsLive);
        Assert.Contains("Błąd", vm.Status);
    }

    [Fact]
    public void ApplyLiveCloudShowsCloudInView()
    {
        var vm = new MainViewModel();
        var cloud = new PointCloud([1f, 2f, 3f]);

        vm.ApplyLiveCloud(cloud);

        Assert.Same(cloud, vm.PointCloud);
    }

    [Fact]
    public async Task SimulatorFeedsLiveCloud()
    {
        var vm = new MainViewModel { LivePort = 34570 };
        var completion = new TaskCompletionSource<PointCloud>();
        vm.LiveCloudUpdated += cloud => completion.TrySetResult(cloud);

        vm.SimulatorEnabled = true;

        Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        vm.SimulatorEnabled = false;
        vm.StopLiveCommand.Execute(null);

        Assert.True(finished == completion.Task, "Symulator nie dostarczyl chmury.");
        Assert.True((await completion.Task).Count > 0);
    }

    [Fact]
    public void CameraStaysInPlaceWhileLive()
    {
        var vm = new MainViewModel { LivePort = 34571 };

        Assert.True(vm.AutoFitCamera);
        Assert.True(vm.AutoResetView);

        vm.StartLiveCommand.Execute(null);

        Assert.False(vm.AutoFitCamera);
        Assert.False(vm.AutoResetView);

        vm.StopLiveCommand.Execute(null);

        Assert.True(vm.AutoFitCamera);
        Assert.True(vm.AutoResetView);
    }

    [Fact]
    public void LiveInfoShowsPacketCounters()
    {
        var vm = new MainViewModel { LivePort = 34572 };

        Assert.Equal(string.Empty, vm.LiveInfo);

        vm.StartLiveCommand.Execute(null);

        Assert.Contains("Punkty:", vm.LiveInfo);
        Assert.Contains("Pakiety:", vm.LiveInfo);

        vm.StopLiveCommand.Execute(null);
    }

    [Fact]
    public void LiveCloudPassesThroughFilters()
    {
        var vm = new MainViewModel();
        vm.Pipeline.AddStage.Execute("sphere-crop");
        var cloud = new PointCloud([1f, 2f, 3f, 4f, 5f, 6f]);

        vm.ApplyLiveCloud(cloud);

        Assert.Same(cloud, vm.Pipeline.Source);
        Assert.Same(vm.Pipeline.Output, vm.PointCloud);
        Assert.NotSame(cloud, vm.PointCloud);
    }

    [Fact]
    public void StatusInfoShowsRatesWhileLive()
    {
        var vm = new MainViewModel { LivePort = 34573 };
        vm.StartLiveCommand.Execute(null);

        Assert.Contains("pkt/s", vm.StatusInfo);
        Assert.Contains("kl./s", vm.StatusInfo);

        vm.StopLiveCommand.Execute(null);
    }
}

public class SensorModelTests
{
    [Fact]
    public void CalibrationLabelFollowsSensorModel()
    {
        var vm = new MainViewModel();
        Assert.Contains("VLP-16", vm.CalibrationLabel);

        vm.SensorModel = VelodyneModel.Hdl32E;

        Assert.Contains("HDL-32E", vm.CalibrationLabel);
    }
}
