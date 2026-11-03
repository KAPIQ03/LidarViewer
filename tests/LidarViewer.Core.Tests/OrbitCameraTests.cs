using System.Numerics;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class OrbitCameraTests
{
    private static Vector3 Ndc(OrbitCamera cam, Vector3 world, float aspect = 1.5f)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), cam.ViewProjection(aspect));
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float eps = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < eps, $"oczekiwano {expected}, jest {actual}");

    [Fact]
    public void Position_FollowsYawAndPitch()
    {
        var cam = new OrbitCamera { Yaw = 0, Pitch = 0 };
        cam.FitTo(new Vector3(1, 2, 3), 4f); // Distance = 10

        AssertNear(new Vector3(11, 2, 3), cam.Position);

        cam.Yaw = MathF.PI / 2;
        AssertNear(new Vector3(1, 12, 3), cam.Position);
    }

    [Fact]
    public void Target_ProjectsToScreenCenterInsideDepthRange()
    {
        var cam = new OrbitCamera();
        cam.FitTo(new Vector3(5, -3, 2), 10f);

        var ndc = Ndc(cam, cam.Target);

        Assert.Equal(0f, ndc.X, 4);
        Assert.Equal(0f, ndc.Y, 4);
        Assert.InRange(ndc.Z, -1f, 1f);
    }

    [Fact]
    public void PointNearCameraIsCloserInDepthThanTarget()
    {
        var cam = new OrbitCamera();
        cam.FitTo(Vector3.Zero, 10f);
        Vector3 towardCamera = Vector3.Normalize(cam.Position - cam.Target);

        float zNear = Ndc(cam, cam.Target + towardCamera * 3f).Z;
        float zTarget = Ndc(cam, cam.Target).Z;

        Assert.True(zNear < zTarget);
        Assert.InRange(zNear, -1f, 1f);
    }

    [Fact]
    public void PointBehindCamera_HasNegativeW()
    {
        var cam = new OrbitCamera();
        cam.FitTo(Vector3.Zero, 10f);
        Vector3 behind = cam.Position + Vector3.Normalize(cam.Position - cam.Target) * 5f;

        var clip = Vector4.Transform(new Vector4(behind, 1), cam.ViewProjection(1.5f));

        Assert.True(clip.W < 0);
    }

    [Fact]
    public void PointToTheRightOfTarget_AppearsOnRightSideOfScreen()
    {
        var cam = new OrbitCamera();
        cam.FitTo(Vector3.Zero, 10f);
        Vector3 forward = Vector3.Normalize(cam.Target - cam.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));

        Assert.True(Ndc(cam, cam.Target + right * 2f).X > 0);
    }

    [Fact]
    public void Rotate_ClampsPitch()
    {
        var cam = new OrbitCamera();

        cam.Rotate(0, 10f);
        Assert.Equal(OrbitCamera.MaxPitch, cam.Pitch);

        cam.Rotate(0, -20f);
        Assert.Equal(-OrbitCamera.MaxPitch, cam.Pitch);
    }

    [Fact]
    public void Zoom_ClampsDistanceToSceneRadiusRange()
    {
        var cam = new OrbitCamera();
        cam.FitTo(Vector3.Zero, 10f);

        cam.Zoom(0.0001f);
        Assert.Equal(0.5f, cam.Distance, 4);

        cam.Zoom(1e9f);
        Assert.Equal(500f, cam.Distance, 2);
    }

    [Fact]
    public void FitTo_SetsTargetAndDistanceProportionalToRadius()
    {
        var cam = new OrbitCamera();

        cam.FitTo(new Vector3(1, 2, 3), 20f);

        Assert.Equal(new Vector3(1, 2, 3), cam.Target);
        Assert.Equal(50f, cam.Distance, 3);
    }

    [Fact]
    public void Pan_MovesTargetPerpendicularlyToViewDirection()
    {
        var cam = new OrbitCamera();
        cam.FitTo(Vector3.Zero, 10f);
        Vector3 forward = Vector3.Normalize(cam.Target - cam.Position);

        cam.Pan(3f, 2f);

        Assert.Equal(0f, Vector3.Dot(cam.Target, forward), 4);
        Assert.Equal(MathF.Sqrt(13f), cam.Target.Length(), 3);
    }

    [Fact]
    public void WorldUnitsPerPixel_ScalesWithDistance()
    {
        var cam = new OrbitCamera();
        cam.FitTo(Vector3.Zero, 10f);

        float a = cam.WorldUnitsPerPixel(500);
        cam.Zoom(2f);

        Assert.Equal(a * 2f, cam.WorldUnitsPerPixel(500), 5);
    }
}
