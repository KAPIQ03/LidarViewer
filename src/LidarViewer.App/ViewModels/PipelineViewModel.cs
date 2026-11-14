using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.Core.Filters;
using LidarViewer.Core.Models;
using CorePipeline = LidarViewer.Core.Filters.Pipeline;
using CoreStage = LidarViewer.Core.Filters.PipelineStage;

namespace LidarViewer.App.ViewModels;

/// <summary>
/// Pipeline filtrow w aplikacji. Lista etapow jest zrodlem danych dla
/// panelu filtrow, a wynik ostatniego przebiegu trafia do widoku chmury.
/// </summary>
public sealed partial class PipelineViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial PointCloud? Source { get; set; }

    [ObservableProperty]
    public partial PointCloud? Output { get; set; }

    [ObservableProperty]
    public partial FilterDescriptor? SelectedAvailableFilter { get; set; }

    [ObservableProperty]
    public partial PipelineStageViewModel? SelectedStage { get; set; }

    [ObservableProperty]
    public partial FilterEditorViewModel? Editor { get; set; }

    public ObservableCollection<PipelineStageViewModel> Stages { get; } = [];

    public IReadOnlyList<FilterDescriptor> AvailableFilters => FilterCatalog.All;

    public IRelayCommand<string> AddStage { get; }

    public IRelayCommand<int> RemoveStage { get; }

    public IRelayCommand<int> MoveStageUp { get; }

    public IRelayCommand<int> MoveStageDown { get; }

    public IRelayCommand<int> ToggleStage { get; }

    public PipelineViewModel()
    {
        AddStage = new RelayCommand<string>(AddStageCore);
        RemoveStage = new RelayCommand<int>(RemoveStageCore);
        MoveStageUp = new RelayCommand<int>(index => MoveStageCore(index, -1));
        MoveStageDown = new RelayCommand<int>(index => MoveStageCore(index, 1));
        ToggleStage = new RelayCommand<int>(ToggleStageCore);

        SelectedAvailableFilter = FilterCatalog.All.FirstOrDefault();
    }

    /// <summary>
    /// Przebudowuje pipeline od nowa i aktualizuje liczniki punktow
    /// przy kazdym etapie.
    /// </summary>
    public void Run()
    {
        if (Source is null)
        {
            Output = null;
            return;
        }

        var pipeline = new CorePipeline();
        foreach (PipelineStageViewModel stage in Stages)
            pipeline.Add(new CoreStage(stage.Name, stage.Settings) { Enabled = stage.Enabled });

        PipelineResult result = pipeline.Run(Source);

        for (int i = 0; i < Stages.Count; i++)
        {
            Stages[i].InputCount = result.Stages[i].InputCount;
            Stages[i].OutputCount = result.Stages[i].OutputCount;
        }

        Output = result.Cloud;
    }

    private void AddStageCore(string? id)
    {
        if (id is null)
            return;

        FilterDescriptor? descriptor = FilterCatalog.Find(id);
        if (descriptor is null)
            return;

        var stage = new PipelineStageViewModel(descriptor);
        stage.Changed += OnStageChanged;
        Stages.Add(stage);
        RefreshIndexes();

        // Wybranie etapu otwiera jego edytor parametrow.
        SelectedStage = stage;
        Run();
    }

    private void RemoveStageCore(int index)
    {
        if (index < 0 || index >= Stages.Count)
            return;

        PipelineStageViewModel stage = Stages[index];
        stage.Changed -= OnStageChanged;
        Stages.RemoveAt(index);
        RefreshIndexes();

        if (SelectedStage == stage)
            SelectedStage = null;

        Run();
    }

    private void MoveStageCore(int index, int offset)
    {
        if (index < 0 || index >= Stages.Count)
            return;

        int target = index + offset;
        if (target < 0 || target >= Stages.Count)
            return;

        Stages.Move(index, target);
        RefreshIndexes();
        Run();
    }

    private void ToggleStageCore(int index)
    {
        if (index < 0 || index >= Stages.Count)
            return;

        Stages[index].Enabled = !Stages[index].Enabled;
    }

    private void RefreshIndexes()
    {
        for (int i = 0; i < Stages.Count; i++)
            Stages[i].Index = i;
    }

    private void OnStageChanged() => Run();

    partial void OnSourceChanged(PointCloud? value) => Run();

    partial void OnSelectedStageChanged(PipelineStageViewModel? value) =>
        Editor = value is null ? null : new FilterEditorViewModel(value);
}
