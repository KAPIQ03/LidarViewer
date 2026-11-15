using LidarViewer.App.ViewModels;

namespace LidarViewer.App.Tests;

public class MainViewModelPanelTests
{
    [Fact]
    public void FiltersPanelStartsOpen()
    {
        var vm = new MainViewModel();

        Assert.True(vm.IsFiltersPanelOpen);
    }

    [Fact]
    public void DiagnosticsPanelStartsClosed()
    {
        var vm = new MainViewModel();

        Assert.False(vm.IsDiagnosticsOpen);
    }

    [Fact]
    public void FiltersPanelCanBeToggled()
    {
        var vm = new MainViewModel();

        vm.IsFiltersPanelOpen = false;

        Assert.False(vm.IsFiltersPanelOpen);
    }

    [Fact]
    public void DiagnosticsPanelCanBeToggled()
    {
        var vm = new MainViewModel();

        vm.IsDiagnosticsOpen = true;

        Assert.True(vm.IsDiagnosticsOpen);
    }
}
