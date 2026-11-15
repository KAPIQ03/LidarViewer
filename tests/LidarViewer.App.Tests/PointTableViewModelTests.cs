using LidarViewer.App.ViewModels;
using LidarViewer.Core.Models;

namespace LidarViewer.App.Tests;

public class PointTableViewModelTests
{
    [Fact]
    public void TableIsNullWhileClosed()
    {
        var vm = new MainViewModel();
        vm.SetSourceForTests(new PointCloud([1f, 2f, 3f]));

        Assert.Null(vm.Table);
    }

    [Fact]
    public void OpeningTableShowsCurrentCloud()
    {
        var vm = new MainViewModel();
        vm.SetSourceForTests(new PointCloud([1f, 2f, 3f, 4f, 5f, 6f]));

        vm.IsTableOpen = true;

        Assert.NotNull(vm.Table);
        Assert.Equal(2, vm.Table!.Count);
    }

    [Fact]
    public void TableFollowsNewClouds()
    {
        var vm = new MainViewModel { IsTableOpen = true };
        Assert.Null(vm.Table);

        vm.SetSourceForTests(new PointCloud([1f, 2f, 3f]));
        Assert.Single(vm.Table!);

        vm.ApplyLiveCloud(new PointCloud([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f]));
        Assert.Equal(3, vm.Table!.Count);
    }

    [Fact]
    public void ClosingTableDropsIt()
    {
        var vm = new MainViewModel { IsTableOpen = true };
        vm.SetSourceForTests(new PointCloud([1f, 2f, 3f]));

        vm.IsTableOpen = false;

        Assert.Null(vm.Table);
    }
}
