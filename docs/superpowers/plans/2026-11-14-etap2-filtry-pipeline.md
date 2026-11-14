# Etap 2 — Filtry i pipeline: Implementation Plan

> **For agentic workers:** każde zadanie to jeden commit, TDD (RED → GREEN → refactor).

**Goal:** sześć filtrów CPU w Core + łańcuch (pipeline) z etapami, które da się włączać, wyłączać i zmieniać bez reloadu pliku, oraz panel UI do tego.

**Architecture:** filtry to czyste statyczne funkcje `PointCloud -> PointCloud` w `LidarViewer.Core/Filters`. Parametry filtrów to rekordy `FilterSettings`; `CloudFilters.Apply` dispatchuje po typu rekordu. `Pipeline` to lista `PipelineStage` (nazwa, ustawienia, flaga włączenia) i `Run` zwraca `PipelineResult` z liczbą punktów po każdym etapie. App ma `PipelineViewModel` (lista etapów) i `FilterEditorViewModel` (edytor parametrów w stylu ParaView: Zastosuj / Anuluj).

**Tech Stack:** .NET 10, C#, xUnit, Avalonia 12.1.4, CommunityToolkit.Mvvm 8.4.2.

---

## Kontrakt API (Core)

Namespace `LidarViewer.Core.Filters`.

```csharp
// Ustawienia filtru - rekordy, jeden na filtr.
public sealed record ScalarRangeSettings(ScalarKind Kind, float Min, float Max);
public sealed record BoxCropSettings(Vector3 Min, Vector3 Max);
public sealed record SphereCropSettings(Vector3 Center, float Radius);
public sealed record VoxelGridSettings(float VoxelSize);
public sealed record StatisticalOutlierSettings(int MeanK, float StdDevThreshold);
public sealed record GroundRemovalSettings(float DistanceThreshold, int Iterations);

// Filtr - czysta funkcja, brak stanu.
public static class ScalarRangeFilter      { public static PointCloud Apply(PointCloud c, ScalarKind k, float min, float max); }
public static class BoxCropFilter          { public static PointCloud Apply(PointCloud c, Vector3 min, Vector3 max); }
public static class SphereCropFilter       { public static PointCloud Apply(PointCloud c, Vector3 center, float radius); }
public static class VoxelGridFilter        { public static PointCloud Apply(PointCloud c, float voxelSize); }
public static class StatisticalOutlierFilter{ public static PointCloud Apply(PointCloud c, int meanK, float stdDevThreshold); }
public static class GroundRemovalFilter    { public static PointCloud Apply(PointCloud c, float distanceThreshold, int iterations); }

// Dispatcher po typu ustawien.
public static class CloudFilters
{
    public static PointCloud Apply(PointCloud cloud, FilterSettings settings);
}

// Katalog filtrów - opis parametrów dla UI.
public enum FilterParameterKind { Number, Choice }

public sealed record FilterParameterSpec(
    string Key, string Label, FilterParameterKind Kind,
    double Default, double Min, double Max,
    IReadOnlyList<string> Choices);

public sealed record FilterDescriptor(
    string Id, string Name, FilterSettings DefaultSettings,
    IReadOnlyList<FilterParameterSpec> Parameters,
    Func<IReadOnlyDictionary<string, double>, FilterSettings> Build);

public static class FilterCatalog
{
    public static IReadOnlyList<FilterDescriptor> All { get; }
    public static FilterDescriptor? Find(string id);
}

// Pipeline.
public sealed class PipelineStage
{
    public string Name { get; }
    public FilterSettings Settings { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed record StageResult(string Name, bool Enabled, int InputCount, int OutputCount);

public sealed record PipelineResult(PointCloud Cloud, IReadOnlyList<StageResult> Stages);

public sealed class Pipeline
{
    public IReadOnlyList<PipelineStage> Stages { get; }
    public void Add(PipelineStage stage);
    public void RemoveAt(int index);
    public void Move(int from, int to);
    public PipelineResult Run(PointCloud source);
}
```

## Semantyka filtrów

| Filtr | Semantyka | Przypadki brzegowe |
|---|---|---|
| `ScalarRangeFilter` | zostawia punkty, dla których `GetAttribute(kind)` mieści się w `[min, max]` (włącznie) | brak atrybutu → pusta chmura; `min > max` → pusta chmura |
| `BoxCropFilter` | zostawia punkty wewnątrz prostopadłościanu `[min, max]` (włącznie) | `min > max` w którejkolwiek osi → pusta chmura |
| `SphereCropFilter` | zostawia punkty o odległości ≤ `radius` od `center` | `radius < 0` → ArgumentOutOfRangeException |
| `VoxelGridFilter` | grupuje punkty w sześciany o boku `voxelSize`, keeppunkt = środek grupy | `voxelSize <= 0` → ArgumentOutOfRangeException |
| `StatisticalOutlierFilter` | zachowuje punkt, jeśli średnia odległość do `meanK` najbliższych sąsiadów ≤ `stdDevThreshold * σ` | `meanK < 1` → ArgumentOutOfRangeException; σ == 0 → zachowuje wszystko |
| `GroundRemovalFilter` | RANSAC dopasowuje płaszczyznę, usuwa punkty bliżej niż `distanceThreshold` od niej | `iterations < 1` → ArgumentOutOfRangeException; `distanceThreshold < 0` → ArgumentOutOfRangeException |

Wszystkie filtry: zachowują `Intensity`, `Category` i `Origin`; wynik to nowa instancja
`PointCloud` (brak mutacji wejścia).

## Wymagania wydajnościowe

- `VoxelGridFilter` i `StatisticalOutlierFilter` na 345 377 punktach: poniżej 2 s.
- `StatisticalOutlierFilter` używa siatki przestrzennej (hash voxelów), nie pełnego
  skanu O(n²). Sąsiedztwo sprawdzane w 27 komórkach siatki wokół punktu.
- `GroundRemovalFilter` dopasowuje płaszczyznę na podpróbie maks. 5000 punktów,
  a potem liczy inliers na całej chmurze.

## Pliki

Core (nowe):
```
src/LidarViewer.Core/Filters/FilterSettings.cs
src/LidarViewer.Core/Filters/ScalarRangeFilter.cs
src/LidarViewer.Core/Filters/BoxCropFilter.cs
src/LidarViewer.Core/Filters/SphereCropFilter.cs
src/LidarViewer.Core/Filters/VoxelGridFilter.cs
src/LidarViewer.Core/Filters/StatisticalOutlierFilter.cs
src/LidarViewer.Core/Filters/GroundRemovalFilter.cs
src/LidarViewer.Core/Filters/CloudFilters.cs
src/LidarViewer.Core/Filters/FilterCatalog.cs
src/LidarViewer.Core/Filters/Pipeline.cs
```

App (nowe):
```
src/LidarViewer.App/ViewModels/PipelineStageViewModel.cs
src/LidarViewer.App/ViewModels/FilterEditorViewModel.cs
src/LidarViewer.App/ViewModels/PipelineViewModel.cs
```

App (zmienione):
```
src/LidarViewer.App/ViewModels/MainViewModel.cs   (SourceCloud, integracja pipeline)
src/LidarViewer.App/Views/MainWindow.axaml        (panel filtrów po prawej)
```

Testy (nowe):
```
tests/LidarViewer.Core.Tests/ScalarRangeFilterTests.cs
tests/LidarViewer.Core.Tests/BoxCropFilterTests.cs
tests/LidarViewer.Core.Tests/SphereCropFilterTests.cs
tests/LidarViewer.Core.Tests/VoxelGridFilterTests.cs
tests/LidarViewer.Core.Tests/StatisticalOutlierFilterTests.cs
tests/LidarViewer.Core.Tests/GroundRemovalFilterTests.cs
tests/LidarViewer.Core.Tests/FilterCatalogTests.cs
tests/LidarViewer.Core.Tests/PipelineTests.cs
tests/LidarViewer.App.Tests/PipelineViewModelTests.cs
```

---

### Task 1: `ScalarRangeFilter` i `BoxCropFilter`

**Files:**
- Create: `src/LidarViewer.Core/Filters/ScalarRangeFilter.cs`
- Create: `src/LidarViewer.Core/Filters/BoxCropFilter.cs`
- Test: `tests/LidarViewer.Core.Tests/ScalarRangeFilterTests.cs`, `tests/LidarViewer.Core.Tests/BoxCropFilterTests.cs`

- [ ] **Step 1: Testy (RED)**

```csharp
public class ScalarRangeFilterTests
{
    private static PointCloud Cloud() => new([0f, 0f, 0f, 10f, 0f, 0f, 20f, 0f, 0f], [1, 2, 3]);

    [Fact]
    public void KeepsOnlyPointsInsideRange()
    {
        var result = ScalarRangeFilter.Apply(Cloud(), ScalarKind.Height, 5f, 15f);
        Assert.Equal(1, result.Count);
        Assert.Equal(10f, result.Positions[0]);
    }

    [Fact]
    public void RangeIsInclusive()
    {
        var result = ScalarRangeFilter.Apply(Cloud(), ScalarKind.Height, 0f, 10f);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MissingAttributeYieldsEmptyCloud()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);
        var result = ScalarRangeFilter.Apply(cloud, ScalarKind.Intensity, 0f, 255f);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void InvertedRangeYieldsEmptyCloud()
    {
        var result = ScalarRangeFilter.Apply(Cloud(), ScalarKind.Height, 15f, 5f);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void PreservesIntensityAndOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 10f, 0f, 0f], [7, 8]) { Origin = (1, 2, 3) };
        var result = ScalarRangeFilter.Apply(cloud, ScalarKind.Height, 5f, 15f);
        Assert.Equal(8, result.Intensity![0]);
        Assert.Equal((1, 2, 3), result.Origin);
    }
}
```

`BoxCropFilterTests`: punkt w środku zostaje, poza X/Y/Z wypada, granica włączna,
`min > max` → pusta chmura.

- [ ] **Step 2: Uruchom i potwierdź RED**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj -v q --nologo`
Expected: błąd kompilacji — brak `LidarViewer.Core.Filters`.

- [ ] **Step 3: Implementacja**

```csharp
using System.Numerics;
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

public static class ScalarRangeFilter
{
    public static PointCloud Apply(PointCloud cloud, ScalarKind kind, float min, float max)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        var attribute = cloud.GetAttribute(kind);
        if (attribute is null || min > max)
            return Empty(cloud);

        int count = cloud.Count;
        int values = Math.Min(count, attribute.Values.Length);

        var keep = new bool[count];
        int kept = 0;
        for (int i = 0; i < values; i++)
        {
            float v = attribute.Values[i];
            if (v < min || v > max) continue;
            keep[i] = true;
            kept++;
        }

        return CloudSubset.Select(cloud, keep, kept);
    }
}
```

Wspólny helper do wyboru podzbioru punktów (`CloudSubset.Select`) dostaje
`Intensity`, `Category` i `Origin` — używany przez wszystkie filtry wycinające.

```csharp
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Filters;

internal static class CloudSubset
{
    public static PointCloud Select(PointCloud cloud, bool[] keep, int kept)
    {
        var positions = new float[kept * 3];
        var intensity = cloud.Intensity is null ? null : new byte[kept];
        var category = cloud.Category is null ? null : new int[kept];

        int o = 0;
        for (int i = 0; i < cloud.Count; i++)
        {
            if (!keep[i]) continue;
            Array.Copy(cloud.Positions, i * 3, positions, o * 3, 3);
            if (intensity is not null) intensity[o] = cloud.Intensity![i];
            if (category is not null) category[o] = cloud.Category![i];
            o++;
        }

        return new PointCloud(positions, intensity, category) { Origin = cloud.Origin };
    }

    public static PointCloud Empty(PointCloud cloud) =>
        new([], null, null) { Origin = cloud.Origin };
}
```

- [ ] **Step 4: Uruchom i potwierdź GREEN** — wszystkie testy Core zielone.

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.Core/Filters tests/LidarViewer.Core.Tests
git commit -m "feat(core): filtry zakresu skalara i przycięcia prostopadłościanem"
```

---

### Task 2: `SphereCropFilter`, `VoxelGridFilter`

**Files:**
- Create: `src/LidarViewer.Core/Filters/SphereCropFilter.cs`, `src/LidarViewer.Core/Filters/VoxelGridFilter.cs`
- Test: `tests/LidarViewer.Core.Tests/SphereCropFilterTests.cs`, `tests/LidarViewer.Core.Tests/VoxelGridFilterTests.cs`

- [ ] **Step 1: Testy (RED)**

`SphereCropFilterTests`: punkt w odległości 5 od środka (0,0,0) zostaje przy
promieniu 5 (włącznie), wypada przy 4.99; `radius < 0` → `ArgumentOutOfRangeException`.

`VoxelGridFilterTests`: dwa punkty w jednym voxelu → jeden punkt (środek grupy);
dwa punkty w różnych voxelach → dwa punkty; `voxelSize <= 0` → wyjątek.

Środek grupy liczony jako średnia arytmetyczna pozycji w voxelu.

- [ ] **Step 2: RED** — brak typów.

- [ ] **Step 3: Implementacja**

`VoxelGridFilter` indeksuje voxele hashtabelą: klucz `(int,int,int)` spakowany
w `long` (po 21 bitów na oś, offset 2^20). Dla każdego voxela sumuje pozycje,
liczy średnią i zapisuje jeden punkt. Środek voxela liczony jest jako średnia
pozycji punktów, co jest tańsze i stabilniejsze niż środek geometryczny kostki.
Kolejność wynikowych punktów: kolejność pierwszego wystąpienia voxela.

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(core): filtry kuli i siatki voxelowej"
```

---

### Task 3: `StatisticalOutlierFilter`

**Files:**
- Create: `src/LidarViewer.Core/Filters/StatisticalOutlierFilter.cs`
- Test: `tests/LidarViewer.Core.Tests/StatisticalOutlierFilterTests.cs`

- [ ] **Step 1: Testy (RED)**

```csharp
[Fact]
public void RemovesIsolatedPoint()
{
    // 9 punktów w kwadracie 1x1 i jeden osobno w odległości 100
    var result = StatisticalOutlierFilter.Apply(cloud, meanK: 4, stdDevThreshold: 1.5f);
    Assert.Equal(9, result.Count);
}

[Fact]
public void KeepsUniformCloud()
{
    var result = StatisticalOutlierFilter.Apply(uniform, 4, 1.5f);
    Assert.Equal(uniform.Count, result.Count);
}

[Fact]
public void MeanKBelowOneThrows() =>
    Assert.Throws<ArgumentOutOfRangeException>(() => StatisticalOutlierFilter.Apply(cloud, 0, 1.5f));
```

- [ ] **Step 2: RED**

- [ ] **Step 3: Implementacja**

Siatka przestrzenna: `cell = voxelSize` z `voxelSize = radiusEstimate`, gdzie
`radiusEstimate` to `meanK`-ta sąsiednia odległość przybliżona jako
`cbrt(3 * objętość / liczba punktów)`. Dla każdego punktu zbierane są sąsiedzi
z 27 komórek wokół jego komórki (nie więcej niż `meanK` najbliższych).
Punkt jest zachowany, gdy średnia odległość do tych sąsiadów nie przekracza
`stdDevThreshold * σ`, gdzie σ to odchylenie standardowe tych średnich.
Gdy σ == 0, zachowywane są wszystkie punkty (brak outlierów).

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(core): filtr statystyczny usuwajacy punkty odosobowane"
```

---

### Task 4: `GroundRemovalFilter`

**Files:**
- Create: `src/LidarViewer.Core/Filters/GroundRemovalFilter.cs`
- Test: `tests/LidarViewer.Core.Tests/GroundRemovalFilterTests.cs`

- [ ] **Step 1: Testy (RED)**

```csharp
[Fact]
public void RemovesPointsOnGroundPlane()
{
    // 100 punktów na płaszczyźnie z = 0 i 50 punktów na płaszczyźnie z = 10
    var result = GroundRemovalFilter.Apply(cloud, distanceThreshold: 0.5f, iterations: 50);
    Assert.Equal(50, result.Count);
}

[Fact]
public void KeepsPointsFarFromPlane() { ... }

[Fact]
public void IterationsBelowOneThrows() =>
    Assert.Throws<ArgumentOutOfRangeException>(() => GroundRemovalFilter.Apply(cloud, 0.5f, 0));
```

- [ ] **Step 2: RED**

- [ ] **Step 3: Implementacja**

RANSAC: losujemy 3 punkty z podpróby (maks. 5000 punktów), dopasowujemy
płaszczyznę metodą najmniejszych kwadratów (SVD 3×3 lub rozwinięcie
wyznacznika), liczymy inliers na całej chmurze, wybieramy najlepszy model.
Powtarzamy `iterations` razy. Po wyborze modelu usuwamy punkty, których
odległość od płaszczyzny jest mniejsza niż `distanceThreshold`.
Ziarno losowania jest stałe (seed = 12345), żeby wynik był powtarzalny
i testowalny.

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(core): filtr usuwajacy ziemie metoda ransac"
```

---

### Task 5: `FilterSettings`, `CloudFilters`, `FilterCatalog`

**Files:**
- Create: `src/LidarViewer.Core/Filters/FilterSettings.cs`, `src/LidarViewer.Core/Filters/CloudFilters.cs`, `src/LidarViewer.Core/Filters/FilterCatalog.cs`
- Test: `tests/LidarViewer.Core.Tests/FilterCatalogTests.cs`

- [ ] **Step 1: Testy (RED)**

```csharp
[Fact]
public void CatalogContainsAllFilters()
{
    Assert.Equal(6, FilterCatalog.All.Count);
    Assert.NotNull(FilterCatalog.Find("scalar-range"));
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
public void ApplyDispatchesToMatchingFilter()
{
    var cloud = new PointCloud([0f, 0f, 0f, 10f, 0f, 0f]);
    var result = CloudFilters.Apply(cloud, new SphereCropSettings(Vector3.Zero, 1f));
    Assert.Equal(1, result.Count);
}
```

- [ ] **Step 2: RED**

- [ ] **Step 3: Implementacja**

`FilterCatalog.All` zawiera 6 deskryptorów:

| Id | Nazwa | Parametry (klucz, etykieta, zakres) |
|---|---|---|
| `scalar-range` | Zakres skalara | `Scalar` (Choice: Intensity/Distance/Height), `Min` (-1e9..1e9), `Max` (-1e9..1e9) |
| `box-crop` | Przycięcie prostopadłościanem | `MinX`, `MinY`, `MinZ`, `MaxX`, `MaxY`, `MaxZ` (-1e9..1e9) |
| `sphere-crop` | Przycięcie kulą | `CenterX`, `CenterY`, `CenterZ` (-1e9..1e9), `Radius` (0..1e9) |
| `voxel-grid` | Siatka voxelowa | `VoxelSize` (0.001..1e6) |
| `statistical-outlier` | Usuwanie odosobionych | `MeanK` (1..1000), `StdDevThreshold` (0..100) |
| `ground-removal` | Usuwanie ziemi | `DistanceThreshold` (0..1e6), `Iterations` (1..1000) |

Wartości domyślne: `Min`/`Max` z zakresu danych chmury są uzupełniane w App
przy dodaniu filtru (katalog w Core zna tylko bezpieczne domyślne wartości).

`CloudFilters.Apply` — `switch` po wzorcu rekordu, `ArgumentOutOfRangeException`
dla nieznanego typu ustawień.

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(core): katalog filtrow i dispatcher ustawien"
```

---

### Task 6: `Pipeline`

**Files:**
- Create: `src/LidarViewer.Core/Filters/Pipeline.cs`
- Test: `tests/LidarViewer.Core.Tests/PipelineTests.cs`

- [ ] **Step 1: Testy (RED)**

```csharp
[Fact]
public void RunsStagesInOrder()
{
    var pipeline = new Pipeline();
    pipeline.Add(new PipelineStage("Box", new BoxCropSettings(new Vector3(-1,-1,-1), new Vector3(1,1,1))));
    pipeline.Add(new PipelineStage("Voxel", new VoxelGridSettings(1f)));
    var result = pipeline.Run(cloud);
    Assert.Equal(2, result.Stages.Count);
    Assert.Equal(cloud.Count, result.Stages[0].InputCount);
}

[Fact]
public void DisabledStageIsSkipped()
{
    var pipeline = new Pipeline();
    var stage = new PipelineStage("Box", ...) { Enabled = false };
    pipeline.Add(stage);
    var result = pipeline.Run(cloud);
    Assert.Equal(cloud.Count, result.Cloud.Count);
    Assert.False(result.Stages[0].Enabled);
}

[Fact]
public void EmptyPipelineReturnsSource()
{
    var result = new Pipeline().Run(cloud);
    Assert.Same(cloud, result.Cloud);
    Assert.Empty(result.Stages);
}

[Fact]
public void MoveReordersStages() { ... }
```

- [ ] **Step 2: RED**

- [ ] **Step 3: Implementacja**

`Pipeline.Run` przechodzi po etapach, dla włączonych stosuje `CloudFilters.Apply`,
zbiera `StageResult(Name, Enabled, InputCount, OutputCount)`. Pusty pipeline
zwraca `source` bez kopiowania (`Assert.Same`).

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(core): pipeline filtrow z licznikiem punktow na etapie"
```

---

### Task 7: `PipelineViewModel` i `FilterEditorViewModel` (App)

**Files:**
- Create: `src/LidarViewer.App/ViewModels/PipelineStageViewModel.cs`, `src/LidarViewer.App/ViewModels/FilterEditorViewModel.cs`, `src/LidarViewer.App/ViewModels/PipelineViewModel.cs`
- Test: `tests/LidarViewer.App.Tests/PipelineViewModelTests.cs`

- [ ] **Step 1: Testy (RED)**

```csharp
[Fact]
public void AddingFilterRunsPipelineAndUpdatesCounts()
{
    var vm = new PipelineViewModel();
    vm.Source = cloud;
    vm.AddStage.Execute("box-crop");
    Assert.Single(vm.Stages);
    Assert.Equal(cloud.Count, vm.Stages[0].InputCount);
}

[Fact]
public void DisablingStageSkipsIt()
{
    var vm = new PipelineViewModel { Source = cloud };
    vm.AddStage.Execute("box-crop");
    vm.Stages[0].Enabled = false;
    Assert.Equal(cloud.Count, vm.Output?.Count);
}

[Fact]
public void RemovingStageRestoresPreviousResult()
{
    var vm = new PipelineViewModel { Source = cloud };
    vm.AddStage.Execute("box-crop");
    vm.RemoveStage.Execute(0);
    Assert.Empty(vm.Stages);
    Assert.Equal(cloud.Count, vm.Output?.Count);
}

[Fact]
public void EditorCancelKeepsPreviousSettings()
{
    var vm = new PipelineViewModel { Source = cloud };
    vm.AddStage.Execute("voxel-grid");
    var editor = vm.Editor!;
    editor.Parameters.First(p => p.Key == "VoxelSize").Number = 99;
    editor.Cancel.Execute(null);
    Assert.NotEqual(99f, ((VoxelGridSettings)vm.Stages[0].Settings).VoxelSize);
}
```

- [ ] **Step 2: RED**

- [ ] **Step 3: Implementacja**

`PipelineViewModel`:
- `Source` (PointCloud?) — wejście pipeline,
- `Output` (PointCloud?) — wynik,
- `Stages` (ObservableCollection<PipelineStageViewModel>),
- `AvailableFilters` (IReadOnlyList<FilterDescriptor>),
- `SelectedAvailableFilter`, `SelectedStage`,
- komendy `AddStage`, `RemoveStage`, `MoveStageUp`, `MoveStageDown`, `ToggleStage`,
- `Editor` (FilterEditorViewModel?) — edytor parametrów zaznaczonego etapu,
- `Run()` — przebudowa pipeline i odświeżenie liczników.

`FilterEditorViewModel`:
- `Descriptor`, `Title`, `Parameters` (ObservableCollection<FilterParameterViewModel>),
- komendy `Apply` (zapisuje ustawienia do etapu i przebudowuje pipeline) i `Cancel`
  (przywraca wartości z etapu).

`FilterParameterViewModel`: `Key`, `Label`, `Kind`, `Number`, `Min`, `Max`, `Choices`,
`SelectedChoice`.

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(app): model pipeline i edytor parametrow filtra"
```

---

### Task 8: Integracja z `MainViewModel` i panel UI

**Files:**
- Modify: `src/LidarViewer.App/ViewModels/MainViewModel.cs`
- Modify: `src/LidarViewer.App/Views/MainWindow.axaml`
- Test: `tests/LidarViewer.App.Tests/MainViewModelPipelineTests.cs`

- [ ] **Step 1: Testy (RED)**

```csharp
[Fact]
public void LoadedFileBecomesPipelineSource()
{
    var vm = new MainViewModel();
    vm.SetSourceForTests(cloud);
    Assert.Same(cloud, vm.SourceCloud);
    Assert.Same(cloud, vm.PointCloud);
}

[Fact]
public void ApplyingFilterUpdatesDisplayedCloud()
{
    var vm = new MainViewModel();
    vm.SetSourceForTests(cloud);
    vm.Pipeline.AddStage.Execute("sphere-crop");
    Assert.NotSame(cloud, vm.PointCloud);
}
```

`SetSourceForTests` to jawne, testowe wejście (produkcja używa komendy Load).

- [ ] **Step 2: RED**

- [ ] **Step 3: Implementacja**

`MainViewModel`:
- nowe `SourceCloud` (wczytany plik, źródło pipeline),
- `PointCloud` to wynik pipeline (to, co widok pokazuje),
- `LoadAsync` ustawia `SourceCloud` i przebudowuje pipeline,
- `Pipeline` to `PipelineViewModel` z podpiętym `SourceCloud`,
- `Status` pokazuje liczbę punktów po filtrach.

`MainWindow.axaml`: główny `DockPanel` trafia do `Grid` z dwiema kolumnami
(`*` i `Auto`); w prawej kolumnie panel filtrów:
- `ComboBox` dostępnych filtrów + przycisk „Dodaj",
- `ListBox` etapów z `CheckBox` (włącz/wyłącz), nazwą, liczbą punktów
  i przyciskami „▲", „▼", „Usuń",
- edytor parametrów z polami liczbowymi, `ComboBox` dla parametru wyboru
  oraz przyciski „Zastosuj" / „Anuluj".

- [ ] **Step 4: GREEN**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(app): panel filtrow w oknie i integracja z pipeline"
```

---

### Task 9: Weryfikacja końcowa etapu 2

**Files:** brak zmian kodu (chyba że testy ujawnią błąd)

- [ ] **Step 1: Pełny build** — `dotnet build LidarViewer.slnx -v q --nologo`, 0 błędów.
- [ ] **Step 2: Pełny zestaw testów** — Core i App zielone.
- [ ] **Step 3: Pokrycie** — Core >= 80%.
- [ ] **Step 4: Lista kontrolna w UI** na `lidar-samples/pdal_autzen_trim.las`:
  1. Wczytanie pliku → 110 000 pkt.
  2. Dodanie „Usuwanie ziemi" → licznik punktów spada, ziemia znika.
  3. Dodanie „Siatka voxelowa" → liczba punktów spada do rozmiaru siatki.
  4. Dodanie „Zakres skalara" z zakresem intensywności → tylko jasne punkty.
  5. Wyłączenie etapu → licznik wraca do poprzedniej wartości.
  6. Usunięcie etapu → wynik wraca do stanu poprzedniego.
  7. Zmiana pliku → pipeline resetuje się do pustego i ładuje nowe dane.
- [ ] **Step 5: Wydajność** — 345 377 pkt przez `VoxelGridFilter` i
  `StatisticalOutlierFilter` poniżej 2 s; UI nie zamraża się.
- [ ] **Step 6: Commit** — tylko jeśli były poprawki.
