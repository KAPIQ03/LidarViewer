using LidarViewer.App.ViewModels;

namespace LidarViewer.App.Tests;

public class LoadDuringLiveTests
{
    [Fact]
    public async Task LoadIsBlockedWhileLiveSessionRuns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lv-live-block-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "x,y,z\n1,2,3\n4,5,6\n");
        var vm = new MainViewModel { LivePort = 34601, FilePath = path };
        try
        {
            vm.StartLiveCommand.Execute(null);
            Assert.True(vm.IsLive);
            Assert.False(vm.CanLoadFile);
            Assert.False(vm.LoadCommand.CanExecute(null));

            await vm.LoadCommand.ExecuteAsync(null);

            Assert.Equal(MainViewModel.LoadBlockedMessage, vm.Status);
            Assert.True(vm.IsLive);
            Assert.Null(vm.SourceCloud);
        }
        finally
        {
            vm.StopLiveCommand.Execute(null);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadWorksAgainAfterLiveStops()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lv-live-unblock-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "x,y,z\n1,2,3\n4,5,6\n");
        var vm = new MainViewModel { LivePort = 34602, FilePath = path };
        try
        {
            vm.StartLiveCommand.Execute(null);
            vm.StopLiveCommand.Execute(null);

            Assert.True(vm.CanLoadFile);
            Assert.True(vm.LoadCommand.CanExecute(null));

            await vm.LoadCommand.ExecuteAsync(null);

            Assert.Equal(2, vm.PointCloud?.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
