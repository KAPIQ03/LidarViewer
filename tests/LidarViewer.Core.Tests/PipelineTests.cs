using System.Numerics;
using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class PipelineTests
{
    [Fact]
    public void RunsStagesInOrder()
    {
        // Filtr statystyczny liczy srednia odleglosc wzgledem aktualnej liczby
        // punktow, wiec kolejnosc etapow zmienia wynik.
        var cloud = new PointCloud([0f, 0f, 0f, 0f, 0f, 0.1f, 10f, 0f, 0f]);
        var outlier = new PipelineStage("outlier", new StatisticalOutlierSettings(1, 0.5f));
        var box = new PipelineStage("box", new BoxCropSettings(new Vector3(1f, -10f, -10f), new Vector3(20f, 10f, 10f)));

        var first = new Pipeline();
        first.Add(outlier);
        first.Add(box);

        var second = new Pipeline();
        second.Add(box);
        second.Add(outlier);

        PipelineResult firstResult = first.Run(cloud);
        PipelineResult secondResult = second.Run(cloud);

        Assert.Equal(2, firstResult.Stages.Count);
        Assert.Equal(0, firstResult.Cloud.Count);

        Assert.Equal(1, secondResult.Stages[0].OutputCount);
        Assert.Equal(1, secondResult.Cloud.Count);
    }

    [Fact]
    public void DisabledStageIsSkipped()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 10f, 0f, 0f]);
        var pipeline = new Pipeline();
        var stage = new PipelineStage("sphere", new SphereCropSettings(Vector3.Zero, 1f));
        stage.Enabled = false;
        pipeline.Add(stage);

        PipelineResult result = pipeline.Run(cloud);

        Assert.Equal(2, result.Cloud.Count);
        Assert.False(result.Stages[0].Enabled);
        Assert.Equal(2, result.Stages[0].InputCount);
        Assert.Equal(2, result.Stages[0].OutputCount);
    }

    [Fact]
    public void EmptyPipelineReturnsSource()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f]);
        var pipeline = new Pipeline();

        PipelineResult result = pipeline.Run(cloud);

        Assert.Same(cloud, result.Cloud);
        Assert.Empty(result.Stages);
    }

    [Fact]
    public void StageReportsInputAndOutputCounts()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 0f, 0f, 5f, 0f, 0f]);
        var pipeline = new Pipeline();
        pipeline.Add(new PipelineStage("box", new BoxCropSettings(new Vector3(-1f, -1f, -1f), new Vector3(2f, 1f, 1f))));
        pipeline.Add(new PipelineStage("sphere", new SphereCropSettings(Vector3.Zero, 0.5f)));

        PipelineResult result = pipeline.Run(cloud);

        Assert.Equal(3, result.Stages[0].InputCount);
        Assert.Equal(2, result.Stages[0].OutputCount);
        Assert.Equal(2, result.Stages[1].InputCount);
        Assert.Equal(1, result.Stages[1].OutputCount);
    }

    [Fact]
    public void StageResultKeepsNameAndEnabledState()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);
        var pipeline = new Pipeline();
        pipeline.Add(new PipelineStage("voxel", new VoxelGridSettings(1f)));

        PipelineResult result = pipeline.Run(cloud);

        Assert.Equal("voxel", result.Stages[0].Name);
        Assert.True(result.Stages[0].Enabled);
    }

    [Fact]
    public void MoveReordersStages()
    {
        var pipeline = new Pipeline();
        pipeline.Add(new PipelineStage("first", new VoxelGridSettings(1f)));
        pipeline.Add(new PipelineStage("second", new VoxelGridSettings(1f)));

        pipeline.Move(1, 0);

        Assert.Equal("second", pipeline.Stages[0].Name);
        Assert.Equal("first", pipeline.Stages[1].Name);
    }

    [Fact]
    public void RemoveAtRemovesStage()
    {
        var pipeline = new Pipeline();
        pipeline.Add(new PipelineStage("first", new VoxelGridSettings(1f)));
        pipeline.Add(new PipelineStage("second", new VoxelGridSettings(1f)));

        pipeline.RemoveAt(0);

        Assert.Single(pipeline.Stages);
        Assert.Equal("second", pipeline.Stages[0].Name);
    }

    [Fact]
    public void MoveWithInvalidIndexThrows()
    {
        var pipeline = new Pipeline();
        pipeline.Add(new PipelineStage("first", new VoxelGridSettings(1f)));

        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Move(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Move(-1, 0));
    }

    [Fact]
    public void AddWithNullStageThrows()
    {
        var pipeline = new Pipeline();

        Assert.Throws<ArgumentNullException>(() => pipeline.Add(null!));
    }

    [Fact]
    public void StageWithNullSettingsThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new PipelineStage("broken", null!));
    }

    [Fact]
    public void RunWithNullSourceThrows()
    {
        var pipeline = new Pipeline();

        Assert.Throws<ArgumentNullException>(() => pipeline.Run(null!));
    }
}
