using System.Numerics;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class PointPickerTests
{
    private static OrbitCamera Camera()
    {
        var cam = new OrbitCamera { Yaw = 0, Pitch = 0 };
        cam.FitTo(Vector3.Zero, 10f); // kamera na +X, patrzy na srodek
        return cam;
    }

    private const int W = 800, H = 600;

    private static (float X, float Y) ToScreen(OrbitCamera cam, Vector3 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), cam.ViewProjection((float)W / H));
        return ((clip.X / clip.W * 0.5f + 0.5f) * W, (1f - (clip.Y / clip.W * 0.5f + 0.5f)) * H);
    }

    [Fact]
    public void PicksPointUnderCursor()
    {
        var cam = Camera();
        float[] points = [0, 0, 0, 0, 5, 0, 0, -5, 0];
        var (x, y) = ToScreen(cam, new Vector3(0, 5, 0));

        int picked = PointPicker.Pick(points, cam.ViewProjection((float)W / H), x + 3, y - 2, W, H);

        Assert.Equal(1, picked);
    }

    [Fact]
    public void ReturnsMinusOneWhenNothingNearby()
    {
        var cam = Camera();
        float[] points = [0, 0, 0];

        int picked = PointPicker.Pick(points, cam.ViewProjection((float)W / H), 10, 10, W, H, tolerancePixels: 12);

        Assert.Equal(-1, picked);
    }

    [Fact]
    public void SkipsPointsBehindCamera()
    {
        var cam = Camera();
        // Kamera stoi na +X (odleglosc 25), wiec punkt na x=100 jest za nia.
        float[] points = [100, 0, 0];

        int picked = PointPicker.Pick(points, cam.ViewProjection((float)W / H), W / 2f, H / 2f, W, H, 1000);

        Assert.Equal(-1, picked);
    }

    [Fact]
    public void OnTieNearerPointWins()
    {
        var cam = Camera();
        // Dwa punkty w jednej linii wzroku: x=0 (dalej) i x=5 (blizej kamery stojacej na +X).
        float[] points = [0, 0, 0, 5, 0, 0];
        var (x, y) = ToScreen(cam, new Vector3(0, 0, 0));

        int picked = PointPicker.Pick(points, cam.ViewProjection((float)W / H), x, y, W, H);

        Assert.Equal(1, picked);
    }

    [Fact]
    public void EmptyCloudGivesMinusOne()
    {
        Assert.Equal(-1, PointPicker.Pick([], Matrix4x4.Identity, 0, 0, W, H));
    }
}
