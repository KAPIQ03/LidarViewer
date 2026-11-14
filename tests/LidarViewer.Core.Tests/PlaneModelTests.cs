using System.Numerics;
using LidarViewer.Core.Spatial;

namespace LidarViewer.Core.Tests;

public class PlaneModelTests
{
    private static void AssertSameDirection(Vector3 actual, Vector3 expected)
    {
        float dot = Vector3.Dot(Vector3.Normalize(actual), Vector3.Normalize(expected));
        Assert.True(Math.Abs(dot) > 0.999f, $"Kierunek {actual} nie jest zgodny z {expected}");
    }

    [Fact]
    public void FitsHorizontalPlane()
    {
        var points = new List<Vector3>();
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 10; j++)
                points.Add(new Vector3(i, j, 0));

        var plane = PlaneModel.FitLeastSquares(points);

        AssertSameDirection(plane.Normal, new Vector3(0, 0, 1));
        Assert.Equal(0f, plane.DistanceTo(new Vector3(3, 4, 0)), 4);
        Assert.Equal(5f, plane.DistanceTo(new Vector3(3, 4, 5)), 4);
    }

    [Fact]
    public void FitsTiltedPlane()
    {
        var points = new List<Vector3>();
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 10; j++)
                points.Add(new Vector3(i, j, 0.1f * i));

        var plane = PlaneModel.FitLeastSquares(points);

        AssertSameDirection(plane.Normal, new Vector3(-0.1f, 0, 1));
        Assert.Equal(0f, plane.DistanceTo(new Vector3(4, 2, 0.4f)), 3);
    }

    [Fact]
    public void FitsVerticalPlane()
    {
        var points = new List<Vector3>();
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 10; j++)
                points.Add(new Vector3(5, i, j));

        var plane = PlaneModel.FitLeastSquares(points);

        AssertSameDirection(plane.Normal, new Vector3(1, 0, 0));
        Assert.Equal(3f, plane.DistanceTo(new Vector3(8, 1, 1)), 4);
    }

    [Fact]
    public void PlaneFromThreePointsPassesThroughThem()
    {
        bool ok = PlaneModel.TryFromThreePoints(
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
            out PlaneModel plane);

        Assert.True(ok);
        Assert.Equal(0f, plane.DistanceTo(new Vector3(0.3f, 0.4f, 0)), 4);
        Assert.Equal(2f, plane.DistanceTo(new Vector3(0.3f, 0.4f, 2)), 4);
    }

    [Fact]
    public void CollinearPointsDoNotDefinePlane()
    {
        bool ok = PlaneModel.TryFromThreePoints(
            new Vector3(0, 0, 0),
            new Vector3(1, 1, 1),
            new Vector3(2, 2, 2),
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void DistanceIsAbsolute()
    {
        var plane = new PlaneModel(new Vector3(0, 0, 1), -2f);

        Assert.Equal(2f, plane.DistanceTo(new Vector3(0, 0, 4)), 4);
        Assert.Equal(2f, plane.DistanceTo(new Vector3(0, 0, 0)), 4);
    }

    [Fact]
    public void FitRequiresThreePoints()
    {
        Assert.Throws<ArgumentException>(() =>
            PlaneModel.FitLeastSquares([Vector3.Zero, Vector3.UnitX]));
    }
}
