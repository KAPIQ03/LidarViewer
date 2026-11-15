using LidarViewer.App.ViewModels;

namespace LidarViewer.App.Tests;

public class MeasurementViewModelTests
{
    [Fact]
    public void InfoIsEmptyWhenToolIsOff()
    {
        var vm = new MainViewModel();

        Assert.Equal(string.Empty, vm.MeasureInfo);
    }

    [Fact]
    public void InfoGuidesUserThroughTwoClicks()
    {
        var vm = new MainViewModel { IsMeasuring = true };
        Assert.Equal("Kliknij punkt chmury.", vm.MeasureInfo);

        vm.MeasuredPointCount = 1;
        Assert.Equal("Wybierz drugi punkt.", vm.MeasureInfo);

        vm.MeasuredPointCount = 2;
        vm.MeasuredDistance = 12.3456;
        Assert.Contains("12", vm.MeasureInfo);
        Assert.StartsWith("Odległość:", vm.MeasureInfo);
    }

    [Fact]
    public void TurningToolOffHidesInfo()
    {
        var vm = new MainViewModel { IsMeasuring = true };
        vm.MeasuredDistance = 5;

        vm.IsMeasuring = false;

        Assert.Equal(string.Empty, vm.MeasureInfo);
    }
}
