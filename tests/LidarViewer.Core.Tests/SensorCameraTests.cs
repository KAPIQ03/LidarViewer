using System.Numerics;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class SensorCameraTests
{
    private static Vector3 Ndc(SensorCamera cam, Vector3 world, float aspect = 1.5f)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), cam.ViewProjection(aspect));
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    [Fact]
    public void DefaultLooksAlongPositiveX()
    {
        var cam = new SensorCamera();

        Assert.Equal(1f, cam.Forward.X, 5);
        Assert.Equal(0f, cam.Forward.Y, 5);
        Assert.Equal(0f, cam.Forward.Z, 5);
    }

    [Fact]
    public void PointInFrontProjectsToScreenCenter()
    {
        var cam = new SensorCamera();
        cam.FitTo(new Vector3(1, 2, 3), 50f);

        var ndc = Ndc(cam, new Vector3(11, 2, 3));

        Assert.Equal(0f, ndc.X, 4);
        Assert.Equal(0f, ndc.Y, 4);
        Assert.InRange(ndc.Z, -1f, 1f);
    }

    [Fact]
    public void PositiveYawTurnsLeftTowardPositiveY()
    {
        var cam = new SensorCamera();
        cam.Rotate(MathF.PI / 2, 0);

        Assert.Equal(1f, cam.Forward.Y, 4);

        // Punkt na +Y trafia teraz na srodek ekranu.
        var ndc = Ndc(cam, new Vector3(0, 10, 0));
        Assert.Equal(0f, ndc.X, 4);
    }

    [Fact]
    public void PointOnLeftAppearsOnLeftOfScreen()
    {
        var cam = new SensorCamera();

        var ndc = Ndc(cam, new Vector3(10, 3, 0)); // +Y to lewa strona przy patrzeniu wzdluz +X

        Assert.True(ndc.X < 0);
    }

    [Fact]
    public void PitchIsClampedBelowNinetyDegrees()
    {
        var cam = new SensorCamera();

        cam.Rotate(0, 10f);
        Assert.Equal(SensorCamera.MaxPitch, cam.Pitch, 5);

        cam.Rotate(0, -20f);
        Assert.Equal(-SensorCamera.MaxPitch, cam.Pitch, 5);
    }

    [Fact]
    public void ZoomChangesFovWithinLimits()
    {
        var cam = new SensorCamera();

        cam.Zoom(0.0001f);
        Assert.Equal(SensorCamera.MinFov, cam.FovY, 5);

        cam.Zoom(100000f);
        Assert.Equal(SensorCamera.MaxFov, cam.FovY, 5);
    }

    [Fact]
    public void ResetRestoresDirectionAndFovButKeepsPosition()
    {
        var cam = new SensorCamera();
        cam.FitTo(new Vector3(4, 5, 6), 20f);
        cam.Rotate(1f, 0.5f);
        cam.Zoom(0.5f);

        cam.Reset();

        Assert.Equal(0f, cam.Yaw);
        Assert.Equal(0f, cam.Pitch);
        Assert.Equal(SensorCamera.DefaultFov, cam.FovY);
        Assert.Equal(new Vector3(4, 5, 6), cam.Position);
    }
}
