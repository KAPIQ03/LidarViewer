using System.Numerics;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

public enum FilterParameterKind
{
    Number,
    Choice,
}

public sealed record FilterParameterSpec(
    string Key,
    string Label,
    FilterParameterKind Kind,
    double Default,
    double Min,
    double Max,
    IReadOnlyList<string> Choices)
{
    public static FilterParameterSpec Number(string key, string label, double min, double max, double value) =>
        new(key, label, FilterParameterKind.Number, value, min, max, []);

    public static FilterParameterSpec Choice(string key, string label, IReadOnlyList<string> choices) =>
        new(key, label, FilterParameterKind.Choice, 0, 0, choices.Count - 1, choices);
}

public sealed record FilterDescriptor(
    string Id,
    string Name,
    FilterSettings DefaultSettings,
    IReadOnlyList<FilterParameterSpec> Parameters,
    Func<IReadOnlyDictionary<string, double>, FilterSettings> Build);

/// <summary>
/// Opis wszystkich filtrow dostepnych w aplikacji: identyfikator, nazwa
/// wyswietlana, domyslne ustawienia i parametry do edycji.
/// </summary>
public static class FilterCatalog
{
    private static readonly string[] ScalarNames = ["Intensity", "Distance", "Height", "LaserId", "Azimuth", "Time"];

    private static readonly FilterParameterSpec[] ScalarRangeParameters =
    [
        FilterParameterSpec.Choice("Scalar", "Skalar", ScalarNames),
        FilterParameterSpec.Number("Min", "Minimum", -1e9, 1e9, 0),
        FilterParameterSpec.Number("Max", "Maksimum", -1e9, 1e9, 255),
    ];

    private static readonly FilterParameterSpec[] BoxCropParameters =
    [
        FilterParameterSpec.Number("MinX", "Min X", -1e9, 1e9, -1e9),
        FilterParameterSpec.Number("MinY", "Min Y", -1e9, 1e9, -1e9),
        FilterParameterSpec.Number("MinZ", "Min Z", -1e9, 1e9, -1e9),
        FilterParameterSpec.Number("MaxX", "Max X", -1e9, 1e9, 1e9),
        FilterParameterSpec.Number("MaxY", "Max Y", -1e9, 1e9, 1e9),
        FilterParameterSpec.Number("MaxZ", "Max Z", -1e9, 1e9, 1e9),
    ];

    private static readonly FilterParameterSpec[] SphereCropParameters =
    [
        FilterParameterSpec.Number("CenterX", "Centrum X", -1e9, 1e9, 0),
        FilterParameterSpec.Number("CenterY", "Centrum Y", -1e9, 1e9, 0),
        FilterParameterSpec.Number("CenterZ", "Centrum Z", -1e9, 1e9, 0),
        FilterParameterSpec.Number("Radius", "Promień", 0, 1e9, 1e9),
    ];

    private static readonly FilterParameterSpec[] VoxelGridParameters =
    [
        FilterParameterSpec.Number("VoxelSize", "Rozmiar voxela", 0.001, 1e6, 0.1),
    ];

    private static readonly FilterParameterSpec[] StatisticalOutlierParameters =
    [
        FilterParameterSpec.Number("MeanK", "Sąsiadów", 1, 1000, 20),
        FilterParameterSpec.Number("StdDevThreshold", "Próg odchylenia", 0, 100, 1.5),
    ];

    private static readonly FilterParameterSpec[] GroundRemovalParameters =
    [
        FilterParameterSpec.Number("DistanceThreshold", "Próg odległości", 0, 1e6, 0.5),
        FilterParameterSpec.Number("Iterations", "Iteracje", 1, 1000, 200),
    ];

    public static IReadOnlyList<FilterDescriptor> All { get; } =
    [
        new FilterDescriptor(
            "scalar-range",
            "Zakres skalara",
            new ScalarRangeSettings(ScalarKind.Intensity, 0, 255),
            ScalarRangeParameters,
            values => new ScalarRangeSettings(
                ScalarAt(Read(values, ScalarRangeParameters[0])),
                (float)Read(values, ScalarRangeParameters[1]),
                (float)Read(values, ScalarRangeParameters[2]))),

        new FilterDescriptor(
            "box-crop",
            "Przycięcie prostopadłościanem",
            new BoxCropSettings(new Vector3(-1e9f, -1e9f, -1e9f), new Vector3(1e9f, 1e9f, 1e9f)),
            BoxCropParameters,
            values => new BoxCropSettings(
                new Vector3(
                    (float)Read(values, BoxCropParameters[0]),
                    (float)Read(values, BoxCropParameters[1]),
                    (float)Read(values, BoxCropParameters[2])),
                new Vector3(
                    (float)Read(values, BoxCropParameters[3]),
                    (float)Read(values, BoxCropParameters[4]),
                    (float)Read(values, BoxCropParameters[5])))),

        new FilterDescriptor(
            "sphere-crop",
            "Przycięcie kulą",
            new SphereCropSettings(Vector3.Zero, 1e9f),
            SphereCropParameters,
            values => new SphereCropSettings(
                new Vector3(
                    (float)Read(values, SphereCropParameters[0]),
                    (float)Read(values, SphereCropParameters[1]),
                    (float)Read(values, SphereCropParameters[2])),
                (float)Read(values, SphereCropParameters[3]))),

        new FilterDescriptor(
            "voxel-grid",
            "Siatka voxelowa",
            new VoxelGridSettings(0.1f),
            VoxelGridParameters,
            values => new VoxelGridSettings((float)Read(values, VoxelGridParameters[0]))),

        new FilterDescriptor(
            "statistical-outlier",
            "Usuwanie odosobionych",
            new StatisticalOutlierSettings(20, 1.5f),
            StatisticalOutlierParameters,
            values => new StatisticalOutlierSettings(
                (int)Math.Round(Read(values, StatisticalOutlierParameters[0])),
                (float)Read(values, StatisticalOutlierParameters[1]))),

        new FilterDescriptor(
            "ground-removal",
            "Usuwanie ziemi",
            new GroundRemovalSettings(0.5f, 200),
            GroundRemovalParameters,
            values => new GroundRemovalSettings(
                (float)Read(values, GroundRemovalParameters[0]),
                (int)Math.Round(Read(values, GroundRemovalParameters[1])))),
    ];

    public static FilterDescriptor? Find(string id) =>
        All.FirstOrDefault(descriptor => descriptor.Id == id);

    private static double Read(IReadOnlyDictionary<string, double> values, FilterParameterSpec spec) =>
        values.TryGetValue(spec.Key, out double value) ? Math.Clamp(value, spec.Min, spec.Max) : spec.Default;

    private static ScalarKind ScalarAt(double index)
    {
        int i = (int)Math.Round(index);
        if (i < 0 || i >= ScalarNames.Length) return ScalarKind.Intensity;
        return (ScalarKind)i;
    }
}
