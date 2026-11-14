using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LidarViewer.Core.Filters;

namespace LidarViewer.App.ViewModels;

/// <summary>
/// Etap pipeline wraz z edytowalnymi parametrami. Ustawienia filtra sa
/// wyliczane z parametrow, dzieki czemu edytor nie musi znac wnetrza
/// poszczegolnych filtrow.
/// </summary>
public sealed partial class PipelineStageViewModel : ObservableObject
{
    private readonly FilterDescriptor _descriptor;
    private Dictionary<string, double> _values;

    public PipelineStageViewModel(FilterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _descriptor = descriptor;
        _values = descriptor.Parameters.ToDictionary(p => p.Key, p => p.Default);
    }

    public FilterDescriptor Descriptor => _descriptor;

    public string Name => _descriptor.Name;

    /// <summary>
    /// Pozycja etapu na liscie - uzywana przez przyciski przenoszenia
    /// i usuwania w panelu filtrow.
    /// </summary>
    public int Index { get; internal set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    [ObservableProperty]
    public partial int InputCount { get; set; }

    [ObservableProperty]
    public partial int OutputCount { get; set; }

    public IReadOnlyDictionary<string, double> Values => new Dictionary<string, double>(_values);

    public FilterSettings Settings => _descriptor.Build(_values);

    /// <summary>
    /// Sygnalizuje, ze etap wymaga ponownego przebudowania pipeline.
    /// </summary>
    public event Action? Changed;

    public void SetValues(IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, double>(values);
        Changed?.Invoke();
    }

    partial void OnEnabledChanged(bool value) => Changed?.Invoke();
}
