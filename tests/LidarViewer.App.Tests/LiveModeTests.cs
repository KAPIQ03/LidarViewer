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
        Assert.True(completion.Task.Result.Count > 0);
    }
}
