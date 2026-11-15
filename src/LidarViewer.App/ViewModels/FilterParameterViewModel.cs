using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LidarViewer.Core.Filters;

namespace LidarViewer.App.ViewModels;

public sealed partial class FilterParameterViewModel : ObservableObject
{
    private readonly FilterParameterSpec _spec;

    public FilterParameterViewModel(FilterParameterSpec spec, double value)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _spec = spec;

        if (spec.Kind == FilterParameterKind.Choice)
            SelectedChoice = (int)Math.Clamp(Math.Round(value), spec.Min, spec.Max);
        else
            Number = Math.Clamp(value, spec.Min, spec.Max);
    }

    public string Key => _spec.Key;

    public string Label => _spec.Label;

    public FilterParameterKind Kind => _spec.Kind;

    public double Min => _spec.Min;

    public double Max => _spec.Max;

    public IReadOnlyList<string> Choices => _spec.Choices;

    [ObservableProperty]
    public partial double Number { get; set; }

    [ObservableProperty]
    public partial int SelectedChoice { get; set; }

    public double Value => Kind == FilterParameterKind.Choice ? SelectedChoice : Number;

    public void SetValue(double value)
    {
        if (Kind == FilterParameterKind.Choice)
            SelectedChoice = (int)Math.Clamp(Math.Round(value), Min, Max);
        else
            Number = Math.Clamp(value, Min, Max);
    }
}
