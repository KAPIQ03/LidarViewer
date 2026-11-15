using System.Numerics;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Rendering;

public static class SensorLocator
{
    public static Vector3? Locate(PointCloud cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        var worldOrigin = new Vector3((float)-cloud.Origin.X, (float)-cloud.Origin.Y, (float)-cloud.Origin.Z);
        return Vector3.Distance(worldOrigin, cloud.Center) <= cloud.Radius ? worldOrigin : null;
    }
}
