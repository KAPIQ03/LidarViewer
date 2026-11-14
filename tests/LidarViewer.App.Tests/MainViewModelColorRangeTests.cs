using System.ComponentModel;
using LidarViewer.App.ViewModels;
using LidarViewer.Core.Models;
using LidarViewer.Core.Rendering;

namespace LidarViewer.App.Tests;

public class MainViewModelColorRangeTests
{
    // Dwa punkty w odleglosci 10 i 250 od sensora, intensywnosci 0 i 255.
    private static PointCloud Cloud(float near, float far) =>
        new([near, 0f, 0f, far, 0f, 0f], [0, 255]);

    private static MainViewModel ViewModelWithCloud(PointCloud cloud) =>
        new() { PointCloud = cloud };

    [Fact]
    public void SwitchToDistance_SeedsRangeFromData()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));

        vm.ColorMode = ColorMode.Distance;

        Assert.Equal(10f, vm.ColorRangeMin);
        Assert.Equal(250f, vm.ColorRangeMax);
    }

    [Fact]
    public void SwitchToIntensity_SeedsRangeFromData()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));

        vm.ColorMode = ColorMode.Intensity;

        Assert.Equal(0f, vm.ColorRangeMin);
        Assert.Equal(255f, vm.ColorRangeMax);
    }

    [Fact]
    public void ManualRange_SurvivesModeSwitch()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));
        vm.AutoColorRange = false;
        vm.ColorRangeMin = 3f;
        vm.ColorRangeMax = 7f;

        vm.ColorMode = ColorMode.Distance;

        Assert.Equal(3f, vm.ColorRangeMin);
        Assert.Equal(7f, vm.ColorRangeMax);
    }

    [Fact]
    public void UntouchedDefaultRange_IsSeededFromData()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));
        vm.AutoColorRange = false;

        vm.ColorMode = ColorMode.Distance;

        Assert.Equal(10f, vm.ColorRangeMin);
        Assert.Equal(250f, vm.ColorRangeMax);
        Assert.Null(vm.ColorWarning);
    }

    [Fact]
    public void ManualEdit_SwitchesOffAutomaticRange()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));

        vm.ColorRangeMin = 3f;

        Assert.False(vm.AutoColorRange);
    }

    [Fact]
    public void SwitchingAutomaticRangeOn_ResetsRangeToData()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));
        vm.ColorMode = ColorMode.Distance;
        vm.AutoColorRange = false;
        vm.ColorRangeMin = 3f;
        vm.ColorRangeMax = 7f;

        vm.AutoColorRange = true;

        Assert.Equal(10f, vm.ColorRangeMin);
        Assert.Equal(250f, vm.ColorRangeMax);
    }

    [Fact]
    public void PublishedRange_IsNeverReversed()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));
        var published = new List<(float Min, float Max)>();
        vm.PropertyChanged += OnRangeChanged(vm, published);

        vm.ColorMode = ColorMode.Distance;

        Assert.NotEmpty(published);
        Assert.All(published, p => Assert.True(p.Max >= p.Min, $"odwrocony zakres {p.Min}..{p.Max}"));
    }

    [Fact]
    public void NewCloud_ResetsManualRangeToNewData()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));
        vm.ColorMode = ColorMode.Distance;
        vm.AutoColorRange = false;
        vm.ColorRangeMin = 3f;
        vm.ColorRangeMax = 7f;

        vm.PointCloud = Cloud(100f, 400f);

        Assert.Equal(100f, vm.ColorRangeMin);
        Assert.Equal(400f, vm.ColorRangeMax);
    }

    [Fact]
    public void Warning_WhenRangeDoesNotCoverData()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));
        vm.ColorMode = ColorMode.Distance;
        vm.AutoColorRange = false;
        vm.ColorRangeMin = 300f;
        vm.ColorRangeMax = 400f;

        Assert.NotNull(vm.ColorWarning);
    }

    [Fact]
    public void Warning_WhenIntensityIsMissing()
    {
        var vm = ViewModelWithCloud(new PointCloud([0f, 0f, 0f]));

        vm.ColorMode = ColorMode.Intensity;

        Assert.NotNull(vm.ColorWarning);
    }

    [Fact]
    public void NoWarning_WhenSolidMode()
    {
        var vm = ViewModelWithCloud(Cloud(10f, 250f));

        Assert.Null(vm.ColorWarning);
        Assert.False(vm.ShowLegend);
    }

    private static PropertyChangedEventHandler OnRangeChanged(MainViewModel vm, List<(float Min, float Max)> published) =>
        (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ColorRangeMin) or nameof(MainViewModel.ColorRangeMax))
                published.Add((vm.ColorRangeMin, vm.ColorRangeMax));
        };
}
