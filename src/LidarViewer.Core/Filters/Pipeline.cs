using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

/// <summary>
/// Pojedynczy etap pipeline: nazwa, ustawienia filtra i stan wlaczenia.
/// </summary>
public sealed class PipelineStage
{
    public PipelineStage(string name, FilterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Name = name;
        Settings = settings;
    }

    public string Name { get; }

    public FilterSettings Settings { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed record StageResult(string Name, bool Enabled, int InputCount, int OutputCount);

public sealed record PipelineResult(PointCloud Cloud, IReadOnlyList<StageResult> Stages);

/// <summary>
/// Lista filtrow nakladanych na chmure w kolejnosci. Kazdy etap raportuje
/// liczbe punktow przed i po filtracji.
/// </summary>
public sealed class Pipeline
{
    private readonly List<PipelineStage> _stages = [];

    public IReadOnlyList<PipelineStage> Stages => _stages;

    public void Add(PipelineStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        _stages.Add(stage);
    }

    public void RemoveAt(int index) => _stages.RemoveAt(index);

    public void Move(int from, int to)
    {
        if (from < 0 || from >= _stages.Count)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (to < 0 || to >= _stages.Count)
            throw new ArgumentOutOfRangeException(nameof(to));

        PipelineStage stage = _stages[from];
        _stages.RemoveAt(from);
        _stages.Insert(to, stage);
    }

    public PipelineResult Run(PointCloud source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var results = new List<StageResult>(_stages.Count);
        PointCloud current = source;

        foreach (PipelineStage stage in _stages)
        {
            int input = current.Count;
            if (stage.Enabled) current = CloudFilters.Apply(current, stage.Settings);

            results.Add(new StageResult(stage.Name, stage.Enabled, input, current.Count));
        }

        return new PipelineResult(current, results);
    }
}
