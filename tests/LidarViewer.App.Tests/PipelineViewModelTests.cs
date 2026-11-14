using LidarViewer.App.ViewModels;
using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.App.Tests;

public class PipelineViewModelTests
{
    // Dwa punkty w tym samym voxelu - filtr siatki voxelowej scala je
    // w jeden, wiec liczba punktow w widoku mowi, czy etap zadzialal.
    private static PointCloud NearbyPoints() => new([0f, 0f, 0f, 0.05f, 0f, 0f]);

    [Fact]
    public void AddingFilterRunsPipelineAndUpdatesCounts()
    {
        var cloud = NearbyPoints();
        var vm = new PipelineViewModel { Source = cloud };

        vm.AddStage.Execute("voxel-grid");

        Assert.Single(vm.Stages);
        Assert.Equal(2, vm.Stages[0].InputCount);
        Assert.Equal(1, vm.Stages[0].OutputCount);
        Assert.Equal(1, vm.Output?.Count);
    }

    [Fact]
    public void DisablingStageSkipsIt()
    {
        var cloud = NearbyPoints();
        var vm = new PipelineViewModel { Source = cloud };
        vm.AddStage.Execute("voxel-grid");

        vm.Stages[0].Enabled = false;

        Assert.Equal(2, vm.Output?.Count);
        Assert.Equal(2, vm.Stages[0].OutputCount);
    }

    [Fact]
    public void RemovingStageRestoresPreviousResult()
    {
        var cloud = NearbyPoints();
        var vm = new PipelineViewModel { Source = cloud };
        vm.AddStage.Execute("voxel-grid");

        vm.RemoveStage.Execute(0);

        Assert.Empty(vm.Stages);
        Assert.Equal(2, vm.Output?.Count);
    }

    [Fact]
    public void EmptyPipelineKeepsSourceCloud()
    {
        var cloud = NearbyPoints();
        var vm = new PipelineViewModel { Source = cloud };

        Assert.Same(cloud, vm.Output);
    }

    [Fact]
    public void UnknownFilterIsIgnored()
    {
        var vm = new PipelineViewModel { Source = NearbyPoints() };

        vm.AddStage.Execute("nie-ma-takiego");

        Assert.Empty(vm.Stages);
    }

    [Fact]
    public void AddingStageOpensEditor()
    {
        var vm = new PipelineViewModel { Source = NearbyPoints() };

        vm.AddStage.Execute("voxel-grid");

        Assert.NotNull(vm.Editor);
        Assert.Equal(vm.Stages[0], vm.SelectedStage);
    }

    [Fact]
    public void EditorCancelKeepsPreviousSettings()
    {
        var vm = new PipelineViewModel { Source = NearbyPoints() };
        vm.AddStage.Execute("voxel-grid");

        var editor = vm.Editor!;
        editor.Parameters.First(p => p.Key == "VoxelSize").Number = 99;
        editor.Cancel.Execute(null);

        Assert.NotEqual(99f, ((VoxelGridSettings)vm.Stages[0].Settings).VoxelSize);
    }

    [Fact]
    public void EditorApplyUpdatesStageSettings()
    {
        var vm = new PipelineViewModel { Source = NearbyPoints() };
        vm.AddStage.Execute("voxel-grid");

        var editor = vm.Editor!;
        editor.Parameters.First(p => p.Key == "VoxelSize").Number = 99;
        editor.Apply.Execute(null);

        var voxel = Assert.IsType<VoxelGridSettings>(vm.Stages[0].Settings);
        Assert.Equal(99f, voxel.VoxelSize);
    }

    [Fact]
    public void EditorApplyRebuildsPipeline()
    {
        var vm = new PipelineViewModel { Source = NearbyPoints() };
        vm.AddStage.Execute("voxel-grid");

        var editor = vm.Editor!;
        editor.Parameters.First(p => p.Key == "VoxelSize").Number = 0.001;
        editor.Apply.Execute(null);

        Assert.Equal(2, vm.Output?.Count);
    }

    [Fact]
    public void MovingStageChangesOrder()
    {
        var vm = new PipelineViewModel { Source = NearbyPoints() };
        vm.AddStage.Execute("voxel-grid");
        vm.AddStage.Execute("sphere-crop");

        vm.MoveStageUp.Execute(1);

        Assert.Equal("Przycięcie kulą", vm.Stages[0].Name);
        Assert.Equal(0, vm.Stages[0].Index);
        Assert.Equal(1, vm.Stages[1].Index);
    }

    [Fact]
    public void ToggleStageSwitchesEnabled()
    {
        var vm = new PipelineViewModel { Source = NearbyPoints() };
        vm.AddStage.Execute("voxel-grid");

        vm.ToggleStage.Execute(0);

        Assert.False(vm.Stages[0].Enabled);
        Assert.Equal(2, vm.Output?.Count);
    }

    [Fact]
    public void ChangingSourceRebuildsPipeline()
    {
        var vm = new PipelineViewModel();
        vm.AddStage.Execute("voxel-grid");

        vm.Source = NearbyPoints();

        Assert.Equal(1, vm.Output?.Count);
    }

    [Fact]
    public void ResetClearsStagesAndKeepsSource()
    {
        var cloud = NearbyPoints();
        var vm = new PipelineViewModel { Source = cloud };
        vm.AddStage.Execute("voxel-grid");

        vm.Reset();

        Assert.Empty(vm.Stages);
        Assert.Null(vm.SelectedStage);
        Assert.Same(cloud, vm.Output);
    }

    [Fact]
    public void StageOrderAffectsResult()
    {
        // Filtr statystyczny liczy srednia odleglosc wzgledem aktualnej
        // liczby punktow, wiec kolejnosc etapow zmienia wynik.
        var cloud = new PointCloud([0f, 0f, 0f, 0f, 0f, 0.1f, 10f, 0f, 0f]);

        Assert.Equal(0, RunOutlierThenBox(cloud).Output?.Count);
        Assert.Equal(1, RunBoxThenOutlier(cloud).Output?.Count);
    }

    private static PipelineViewModel RunOutlierThenBox(PointCloud cloud)
    {
        var vm = new PipelineViewModel { Source = cloud };

        vm.AddStage.Execute("statistical-outlier");
        ApplyParameters(vm, ("MeanK", 1), ("StdDevThreshold", 0.5));

        vm.AddStage.Execute("box-crop");
        ApplyParameters(vm, ("MinX", 1));

        return vm;
    }

    private static PipelineViewModel RunBoxThenOutlier(PointCloud cloud)
    {
        var vm = new PipelineViewModel { Source = cloud };

        vm.AddStage.Execute("box-crop");
        ApplyParameters(vm, ("MinX", 1));

        vm.AddStage.Execute("statistical-outlier");
        ApplyParameters(vm, ("MeanK", 1), ("StdDevThreshold", 0.5));

        return vm;
    }

    private static void ApplyParameters(PipelineViewModel vm, params (string Key, double Value)[] parameters)
    {
        var editor = vm.Editor!;
        foreach ((string key, double value) in parameters)
            editor.Parameters.First(p => p.Key == key).Number = value;

        editor.Apply.Execute(null);
    }
}
