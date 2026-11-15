using System.Numerics;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class SceneGuidesTests
{
    [Theory]
    [InlineData(60f, 10f)]
    [InlineData(100f, 20f)]
    [InlineData(6f, 1f)]
    [InlineData(0.6f, 0.1f)]
    [InlineData(300f, 50f)]
    public void StepFollowsOneTwoFiveSeries(float radius, float expected)
    {
        Assert.Equal(expected, SceneGuides.NiceStep(radius), 4);
    }

    [Fact]
    public void GridCoversWholeSceneWithReasonableRingCount()
    {
        var guides = SceneGuides.Build(Vector3.Zero, 100f);

        Assert.InRange(guides.RingCount, 4, 12);
        Assert.True(guides.RingCount * guides.RingStep >= 100f);
    }

    [Fact]
    public void AxesStartAtOriginAndPointAlongEachAxis()
    {
        var origin = new Vector3(1, 2, 3);
        var guides = SceneGuides.Build(origin, 50f);

        Assert.Equal(6, guides.AxisVertexCount);
        float[] p = guides.AxisPositions;
        Assert.Equal([1f, 2f, 3f], p[0..3]);
        Assert.True(p[3] > 1f && p[4] == 2f && p[5] == 3f);
        Assert.True(p[9] == 1f && p[10] > 2f && p[11] == 3f);
        Assert.True(p[15] == 1f && p[16] == 2f && p[17] > 3f);
    }

    [Fact]
    public void GridLiesInHorizontalPlaneThroughOrigin()
    {
        var guides = SceneGuides.Build(new Vector3(0, 0, 5), 20f);

        for (int i = 2; i < guides.GridPositions.Length; i += 3)
            Assert.Equal(5f, guides.GridPositions[i]);
    }

    [Fact]
    public void ColorsMatchVertexCountsAndAreOpaque()
    {
        var guides = SceneGuides.Build(Vector3.Zero, 20f);

        Assert.Equal(guides.AxisVertexCount * 4, guides.AxisColors.Length);
        Assert.Equal(guides.GridVertexCount * 4, guides.GridColors.Length);
        Assert.All(guides.GridColors.Where((_, i) => i % 4 == 3), a => Assert.Equal(255, a));
        Assert.Equal(0, guides.GridVertexCount % 2);
    }

    [Fact]
    public void TinyOrZeroRadiusDoesNotThrow()
    {
        var guides = SceneGuides.Build(Vector3.Zero, 0f);

        Assert.True(guides.RingCount >= 1);
        Assert.True(guides.GridVertexCount > 0);
    }
}
