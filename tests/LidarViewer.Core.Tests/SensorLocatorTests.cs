using System.Numerics;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Readers;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class SensorLocatorTests
{
    private static PointCloud Ring() =>
        new([10f, 0, 0, -10f, 0, 0, 0, 10f, 0, 0, -10f, 0]);

    [Fact]
    public void SensorAtOriginWhenInsideCloud() =>
        Assert.Equal(Vector3.Zero, SensorLocator.Locate(Ring()));

    [Fact]
    public void NullWhenOriginFarFromCloud()
    {
        var cloud = new PointCloud([1000f, 1000, 0, 1001f, 1000, 0]);

        Assert.Null(SensorLocator.Locate(cloud));
    }

    [Fact]
    public void SensorStaysAtWorldOriginAfterLasRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".las");
        try
        {
            LasWriter.Write(path, Ring());
            PointCloud loaded = PointCloudLoader.Read(path);

            // Wczytana chmura ma przesuniety uklad (Origin), ale czujnik nadal jest w srodku pierscienia.
            Vector3 sensor = SensorLocator.Locate(loaded)!.Value;
            Assert.True(Vector3.Distance(sensor, loaded.Center) < 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LasRoundTripKeepsPointsRelativeToSensor()
    {
        // Chmura niesymetryczna wzgledem czujnika (jak realne nagranie z jedna sciana).
        float[] original = [2f, 3f, 1f, 30f, 40f, 2f, -1f, 25f, 0.5f, 15f, -2f, 3f];
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".las");
        try
        {
            LasWriter.Write(path, new PointCloud(original));
            PointCloud loaded = PointCloudLoader.Read(path);

            Vector3 sensor = SensorLocator.Locate(loaded)!.Value;
            for (int i = 0; i < original.Length; i++)
                Assert.Equal(original[i], loaded.Positions[i] - sensor[i % 3], 2);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
