using LidarViewer.App.ViewModels;
using LidarViewer.Core.Models;

namespace LidarViewer.App.Tests;

public class MainViewModelPipelineTests
{
    private static PointCloud Sample() => new([0f, 0f, 0f, 1f, 0f, 0f, 5f, 0f, 0f]);

    [Fact]
    public void LoadedFileBecomesPipelineSource()
    {
        var cloud = Sample();
        var vm = new MainViewModel();

        vm.SetSourceForTests(cloud);

        Assert.Same(cloud, vm.SourceCloud);
        Assert.Same(cloud, vm.PointCloud);
        Assert.Same(cloud, vm.Pipeline.Source);
    }

    [Fact]
    public void ApplyingFilterUpdatesDisplayedCloud()
    {
        var cloud = Sample();
        var vm = new MainViewModel();
        vm.SetSourceForTests(cloud);

        vm.Pipeline.AddStage.Execute("sphere-crop");

        Assert.NotSame(cloud, vm.PointCloud);
        Assert.Same(cloud, vm.SourceCloud);
    }

    [Fact]
    public void RemovingFilterRestoresSourceCloud()
    {
        var cloud = Sample();
        var vm = new MainViewModel();
        vm.SetSourceForTests(cloud);
        vm.Pipeline.AddStage.Execute("sphere-crop");

        vm.Pipeline.RemoveStage.Execute(0);

        Assert.Same(cloud, vm.PointCloud);
    }

    [Fact]
    public void StatusShowsPointCountAfterFilters()
    {
        var vm = new MainViewModel();
        vm.SetSourceForTests(Sample());

        vm.Pipeline.AddStage.Execute("sphere-crop");

        Assert.Contains("Po filtrach", vm.Status);
        Assert.Contains("3", vm.Status);
    }

    [Fact]
    public void FilterRemovesPointsFromDisplayedCloud()
    {
        var vm = new MainViewModel();
        vm.SetSourceForTests(Sample());
        vm.Pipeline.AddStage.Execute("voxel-grid");

        var editor = vm.Pipeline.Editor!;
        editor.Parameters.First(p => p.Key == "VoxelSize").Number = 10;
        editor.Apply.Execute(null);

        Assert.Equal(1, vm.PointCloud?.Count);
    }
}
