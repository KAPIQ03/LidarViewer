using System.Numerics;
using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class FilterCatalogTests
{
    [Fact]
    public void CatalogContainsAllFilters()
    {
        Assert.Equal(6, FilterCatalog.All.Count);
        Assert.NotNull(FilterCatalog.Find("scalar-range"));
        Assert.NotNull(FilterCatalog.Find("box-crop"));
        Assert.NotNull(FilterCatalog.Find("sphere-crop"));
        Assert.NotNull(FilterCatalog.Find("voxel-grid"));
        Assert.NotNull(FilterCatalog.Find("statistical-outlier"));
        Assert.NotNull(FilterCatalog.Find("ground-removal"));
        Assert.Null(FilterCatalog.Find("nie-ma-takiego"));
    }

    [Fact]
    public void BuildProducesMatchingSettings()
    {
        var descriptor = FilterCatalog.Find("box-crop")!;
        var settings = descriptor.Build(new Dictionary<string, double>
        {
            ["MinX"] = -1, ["MinY"] = -2, ["MinZ"] = -3,
            ["MaxX"] = 1, ["MaxY"] = 2, ["MaxZ"] = 3,
        });

        var box = Assert.IsType<BoxCropSettings>(settings);
        Assert.Equal(-1f, box.Min.X);
        Assert.Equal(-3f, box.Min.Z);
        Assert.Equal(3f, box.Max.Z);
    }

    [Fact]
    public void EveryFilterHasBuildableDefaults()
    {
        foreach (var descriptor in FilterCatalog.All)
        {
            var values = descriptor.Parameters.ToDictionary(p => p.Key, p => p.Default);
            Assert.NotNull(descriptor.Build(values));
        }
    }

    [Fact]
    public void EveryFilterHasBuildableDefaultsMatchingItsDescriptor()
    {
        foreach (var descriptor in FilterCatalog.All)
        {
            var values = descriptor.Parameters.ToDictionary(p => p.Key, p => p.Default);
            var settings = descriptor.Build(values);

            Assert.Equal(descriptor.DefaultSettings.GetType(), settings.GetType());
        }
    }

    [Fact]
    public void ApplyDispatchesToMatchingFilter()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 10f, 0f, 0f]);

        var result = CloudFilters.Apply(cloud, new SphereCropSettings(Vector3.Zero, 1f));

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public void ChoiceParameterSelectsScalarKind()
    {
        var descriptor = FilterCatalog.Find("scalar-range")!;

        var settings = descriptor.Build(new Dictionary<string, double>
        {
            ["Scalar"] = 2, ["Min"] = 0, ["Max"] = 10,
        });

        var scalar = Assert.IsType<ScalarRangeSettings>(settings);
        Assert.Equal(ScalarKind.Height, scalar.Kind);
        Assert.Equal(10f, scalar.Max);
    }

    [Fact]
    public void IntegerParametersAreRounded()
    {
        var descriptor = FilterCatalog.Find("statistical-outlier")!;

        var settings = descriptor.Build(new Dictionary<string, double>
        {
            ["MeanK"] = 12.6, ["StdDevThreshold"] = 2,
        });

        var outlier = Assert.IsType<StatisticalOutlierSettings>(settings);
        Assert.Equal(13, outlier.MeanK);
        Assert.Equal(2f, outlier.StdDevThreshold);
    }

    [Fact]
    public void ValuesOutsideRangeAreClamped()
    {
        var descriptor = FilterCatalog.Find("statistical-outlier")!;

        var settings = descriptor.Build(new Dictionary<string, double>
        {
            ["MeanK"] = 99999, ["StdDevThreshold"] = -5,
        });

        var outlier = Assert.IsType<StatisticalOutlierSettings>(settings);
        Assert.Equal(1000, outlier.MeanK);
        Assert.Equal(0f, outlier.StdDevThreshold);
    }

    [Fact]
    public void MissingValuesFallBackToDefaults()
    {
        var descriptor = FilterCatalog.Find("voxel-grid")!;

        var settings = descriptor.Build(new Dictionary<string, double>());

        var voxel = Assert.IsType<VoxelGridSettings>(settings);
        Assert.Equal(descriptor.DefaultSettings, voxel);
    }

    [Fact]
    public void EveryDescriptorHasUniqueIdAndName()
    {
        var ids = FilterCatalog.All.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        var names = FilterCatalog.All.Select(d => d.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void EveryParameterHasLabelAndValidRange()
    {
        foreach (var descriptor in FilterCatalog.All)
        {
            foreach (var parameter in descriptor.Parameters)
            {
                Assert.False(string.IsNullOrWhiteSpace(parameter.Label));
                Assert.True(parameter.Min <= parameter.Max);
                Assert.True(parameter.Default >= parameter.Min && parameter.Default <= parameter.Max);

                if (parameter.Kind == FilterParameterKind.Choice)
                    Assert.NotEmpty(parameter.Choices);
                else
                    Assert.Empty(parameter.Choices);
            }
        }
    }

    [Fact]
    public void UnknownSettingsTypeThrows()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CloudFilters.Apply(cloud, new UnknownSettings()));
    }

    private sealed record UnknownSettings : FilterSettings;
}
