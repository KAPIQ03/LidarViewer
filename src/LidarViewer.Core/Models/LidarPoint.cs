namespace LidarViewer.Core.Models;

public readonly record struct LidarPoint(double X, double Y, double Z, double Intensity = 0);
