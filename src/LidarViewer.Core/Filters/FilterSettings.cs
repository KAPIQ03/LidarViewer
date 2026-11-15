using System.Numerics;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

/// <summary>
/// Ustawienia pojedynczego filtra. Kazdy filtr ma wlasny rekord, dzieki
/// czemu dispatcher rozpoznaje go po typie.
/// </summary>
public abstract record FilterSettings;

public sealed record ScalarRangeSettings(ScalarKind Kind, float Min, float Max) : FilterSettings;

public sealed record BoxCropSettings(Vector3 Min, Vector3 Max) : FilterSettings;

public sealed record SphereCropSettings(Vector3 Center, float Radius) : FilterSettings;

public sealed record VoxelGridSettings(float VoxelSize) : FilterSettings;

public sealed record StatisticalOutlierSettings(int MeanK, float StdDevThreshold) : FilterSettings;

public sealed record GroundRemovalSettings(float DistanceThreshold, int Iterations) : FilterSettings;
