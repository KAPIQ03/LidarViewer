using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.Core.Filters;

namespace LidarViewer.App.ViewModels;

/// <summary>
/// Edytor parametrow pojedynczego etapu. Zmiany sa zapisywane dopiero po
/// nacisnieciu "Zastosuj", a "Anuluj" przywraca wartosci etapu.
/// </summary>
public sealed class FilterEditorViewModel
{
    private readonly PipelineStageViewModel _stage;

    public FilterEditorViewModel(PipelineStageViewModel stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        _stage = stage;

        IReadOnlyDictionary<string, double> values = stage.Values;
        Parameters = new ObservableCollection<FilterParameterViewModel>(
            stage.Descriptor.Parameters.Select(p => new FilterParameterViewModel(
                p,
                values.TryGetValue(p.Key, out double value) ? value : p.Default)));

        Apply = new RelayCommand(ApplyCore);
        Cancel = new RelayCommand(CancelCore);
    }

    public string Title => _stage.Descriptor.Name;

    public ObservableCollection<FilterParameterViewModel> Parameters { get; }

    public IRelayCommand Apply { get; }

    public IRelayCommand Cancel { get; }

    private void ApplyCore()
    {
        var values = new Dictionary<string, double>();
        foreach (FilterParameterViewModel parameter in Parameters)
            values[parameter.Key] = parameter.Value;

        _stage.SetValues(values);
    }

    private void CancelCore()
    {
        IReadOnlyDictionary<string, double> current = _stage.Values;
        foreach (FilterParameterViewModel parameter in Parameters)
            if (current.TryGetValue(parameter.Key, out double value))
                parameter.SetValue(value);
    }
}
