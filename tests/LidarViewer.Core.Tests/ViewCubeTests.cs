using System.Numerics;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class ViewCubeTests
{
    [Fact]
    public void LookingFromAboveShowsTopFaceDominant()
    {
        var faces = ViewCube.Project(MathF.PI / 4, OrbitCamera.MaxPitch, 100);

        Assert.Equal("Z", faces[^1].Label);
    }

    [Fact]
    public void ShowsThreeFacesFromGenericAngle()
    {
        var faces = ViewCube.Project(MathF.PI / 4, MathF.PI / 6, 100);

        Assert.Equal(3, faces.Count);
        Assert.Contains(faces, f => f.Label == "X");
        Assert.Contains(faces, f => f.Label == "Y");
        Assert.Contains(faces, f => f.Label == "Z");
    }

    [Fact]
    public void FacesStayInsideBounds()
    {
        foreach (float yaw in new[] { 0f, 1f, 2.5f, -2f })
        foreach (float pitch in new[] { -1f, 0f, 0.7f })
        foreach (var face in ViewCube.Project(yaw, pitch, 100))
        foreach (Vector2 c in face.Corners)
        {
            Assert.InRange(c.X, 0, 100);
            Assert.InRange(c.Y, 0, 100);
        }
    }

    [Fact]
    public void HitTestAtFaceCenterReturnsThatFace()
    {
        var faces = ViewCube.Project(MathF.PI / 4, MathF.PI / 6, 100);

        foreach (var face in faces)
            Assert.Same(face, ViewCube.HitTest(faces, face.Center));
    }

    [Fact]
    public void HitTestOutsideReturnsNull()
    {
        var faces = ViewCube.Project(MathF.PI / 4, MathF.PI / 6, 100);

        Assert.Null(ViewCube.HitTest(faces, new Vector2(-5, -5)));
    }

    [Fact]
    public void LookDirectionPointsFromFaceToCenter()
    {
        var faces = ViewCube.Project(0.3f, 0.3f, 100);
        var x = faces.Single(f => f.Label == "X");

        Assert.Equal(-Vector3.UnitX, x.LookDirection);
    }

    [Fact]
    public void FrontFaceMatchesLookAlong()
    {
        var camera = new OrbitCamera();
        camera.LookAlong(-Vector3.UnitX);

        var faces = ViewCube.Project(camera.Yaw, camera.Pitch, 100);

        Assert.Equal("X", faces[^1].Label);
    }
}
