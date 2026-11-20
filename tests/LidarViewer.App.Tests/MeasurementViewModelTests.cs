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

public class SensorMeasurementViewModelTests
{
    [Fact]
    public void MeasuringFromSensorTurnsToolOnAndShowsUnits()
    {
        var vm = new MainViewModel { MeasureFromSensor = true };

        Assert.True(vm.IsMeasuring);
        Assert.Contains("środka lidaru", vm.MeasureInfo);

        vm.MeasuredPointCount = 2;
        vm.MeasuredDistance = 0.42;
        Assert.Equal("Odległość od lidaru: 42 cm", vm.MeasureInfo);
    }

    [Fact]
    public void TurningToolOffAlsoDisablesSensorMode()
    {
        var vm = new MainViewModel { MeasureFromSensor = true };

        vm.IsMeasuring = false;

        Assert.False(vm.MeasureFromSensor);
        Assert.Equal(string.Empty, vm.MeasureInfo);
    }

    [Fact]
    public void TwoPointDistanceIsShownInMetres()
    {
        var vm = new MainViewModel { IsMeasuring = true, MeasuredPointCount = 2, MeasuredDistance = 12.3456 };

        Assert.Equal("Odległość: 12,35 m", vm.MeasureInfo);
    }
}
