using System.Numerics;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class ScreenProjectorTests
{
    [Fact]
    public void CenterOfViewMapsToMiddleOfViewport()
    {
        var screen = ScreenProjector.Project(Vector3.Zero, Matrix4x4.Identity, 200, 100);

        Assert.NotNull(screen);
        Assert.Equal(100f, screen.Value.X, 3);
        Assert.Equal(50f, screen.Value.Y, 3);
    }

    [Fact]
    public void PointOutsideFrustumIsHidden()
    {
        Assert.Null(ScreenProjector.Project(new Vector3(2f, 0f, 0f), Matrix4x4.Identity, 200, 100));
    }
}
