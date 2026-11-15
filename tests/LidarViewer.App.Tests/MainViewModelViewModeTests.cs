using System.ComponentModel;
using LidarViewer.App.ViewModels;

namespace LidarViewer.App.Tests;

public class MainViewModelViewModeTests
{
    [Fact]
    public void SwitchTo2D_RaisesIs2DAndIs3D()
    {
        var vm = new MainViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += OnPropertyChanged(changed);

        vm.ViewMode = ViewMode.Widok2D;

        Assert.True(vm.Is2D);
        Assert.False(vm.Is3D);
        Assert.Contains(nameof(MainViewModel.Is2D), changed);
        Assert.Contains(nameof(MainViewModel.Is3D), changed);
    }

    [Fact]
    public void SwitchTo3D_RaisesIs2DAndIs3D()
    {
        var vm = new MainViewModel { ViewMode = ViewMode.Widok2D };
        var changed = new List<string>();
        vm.PropertyChanged += OnPropertyChanged(changed);

        vm.ViewMode = ViewMode.Widok3D;

        Assert.False(vm.Is2D);
        Assert.True(vm.Is3D);
        Assert.Contains(nameof(MainViewModel.Is2D), changed);
        Assert.Contains(nameof(MainViewModel.Is3D), changed);
    }

    private static PropertyChangedEventHandler OnPropertyChanged(List<string> changed) =>
        (_, e) =>
        {
            if (e.PropertyName is not null)
                changed.Add(e.PropertyName);
        };
}
