# Etap 1 — Kolory i atrybuty punktów: Plan implementacji

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chmura punktów renderowana w 3D i 2D z kolorem per punkt — jednolitym, wg intensywności lub wg odległości (range) — plus legenda i sterowanie zakresem kolorów w UI.

**Architecture:** Core (bez Avalonia) dostaje warstwę skalarnych: `PointCloud.GetAttribute(ScalarKind)` zwraca nazwany atrybut `float[]` z policzonym Min/Max; intensywność jest konwertowana z bajtów, odległość liczona jest jako `|Positions + Origin|`. Kolory powstają na CPU w `CloudColors.Build` jako bufor RGBA; GPU dostaje osobny VBO na kolory, dzięki czemu zmiana trybu kolorowania nie przepisuje pozycji. UI wystawia tryb, mapę kolorów, zakres (ręczny lub dopasowany do danych) i legendę.

**Tech Stack:** .NET 10, Avalonia 12.1.4, Silk.NET.OpenGL 2.23.0, CommunityToolkit.Mvvm 8.4.2, xUnit 2.9.3.

---

## Semantyka odległości (ważne — nie pomylić)

`PointCloud.Origin` to **światowa pozycja początku układu współrzędnych, w którym zapisano plik**. Zachodzi relacja:

```
world = Positions + Origin
```

`LasReader` przesuwa pozycje o minimalny narożnik (żeby zachować precyzję float32 dla plików georeferencjonowanych) i ustawia `Origin` na pozycję tego narożnika w układzie świata. Dlatego:

```
range = |world| = |Positions + Origin|
```

Dla plików w układzie sensora (offset = 0, np. `pdal_autzen_trim.las`) to dokładnie **zasięg pomiaru od sensora**. Dla plików georeferencjonowanych (offset UTM, np. `laspy_vegetation_1_3.las`) to odległość od lokalnego początku układu — wartości rzędu 10^5 m. Dla klatek live z Velodyne `Origin = (0,0,0)`, więc `range = |p|`.

**Kod referencyjny dla testu:**
```csharp
var cloud = new PointCloud([0f, 0f, 0f,  3f, 4f, 0f]) { Origin = (10, 0, 0) };
// world punktów: (10,0,0) i (13,4,0) -> range: 10 i sqrt(169+16)=sqrt(185)
```

## Struktura plików

**Core — nowe:**
| Plik | Odpowiedzialność |
|---|---|
| `src/LidarViewer.Core/Models/PointAttribute.cs` | Nazwany atrybut punktów: `float[]` + Min/Max |
| `src/LidarViewer.Core/Models/ScalarKind.cs` | Enum: `Intensity`, `Distance`, `Height` |
| `src/LidarViewer.Core/Rendering/Rgb.cs` | `readonly record struct Rgb(byte R, byte G, byte B)` |
| `src/LidarViewer.Core/Rendering/Rgba.cs` | `readonly record struct Rgba(byte R, byte G, byte B, byte A)` |
| `src/LidarViewer.Core/Rendering/ColormapKind.cs` | Enum: `Grayscale`, `Viridis`, `Turbo`, `Jet` |
| `src/LidarViewer.Core/Rendering/Colormaps.cs` | Budowa tablicy LUT 256×RGB z tablic kotwic |
| `src/LidarViewer.Core/Rendering/ColorRamp.cs` | Mapowanie wartość → kolor z zakresem [Min, Max] |
| `src/LidarViewer.Core/Rendering/ColorMode.cs` | Enum: `Solid`, `Intensity`, `Distance` |
| `src/LidarViewer.Core/Rendering/ColorSettings.cs` | Rekord ustawień kolorowania (Mode, Map, Min, Max, Solid) |
| `src/LidarViewer.Core/Rendering/CloudColors.cs` | `Build(cloud, settings) -> byte[]` (RGBA, 4 B na punkt) |

**Core — modyfikowane:**
| Plik | Zmiana |
|---|---|
| `src/LidarViewer.Core/Models/PointCloud.cs` | `GetAttribute(ScalarKind)`, implementacje Intensity/Distance/Height, cache |

**App — nowe:**
| Plik | Odpowiedzialność |
|---|---|
| `src/LidarViewer.App/Controls/ColorLegend.cs` | Legenda: tytuł, pasek gradientu, podpisy min/max |

**App — modyfikowane:**
| Plik | Zmiana |
|---|---|
| `src/LidarViewer.App/Controls/PointCloudView3D.cs` | VBO kolorów, atrybut `aColor` w shaderze, `ColorSettings` |
| `src/LidarViewer.App/Controls/PointCloudView.cs` | Kolor per punkt w rastrze 2D (z RGBA na BGRA) |
| `src/LidarViewer.App/ViewModels/MainViewModel.cs` | Stan kolorów, auto-zakres, `ColorSettings`, `LegendTitle` |
| `src/LidarViewer.App/Views/MainWindow.axaml` | Kontrolki kolorów + legenda |

**Testy — nowe:**
`tests/LidarViewer.Core.Tests/PointAttributeTests.cs`, `ScalarFieldsTests.cs`, `ColormapsTests.cs`, `ColorRampTests.cs`, `CloudColorsTests.cs`

---

### Task 1: `PointAttribute` i `ScalarKind`

**Files:**
- Create: `src/LidarViewer.Core/Models/PointAttribute.cs`
- Create: `src/LidarViewer.Core/Models/ScalarKind.cs`
- Test: `tests/LidarViewer.Core.Tests/PointAttributeTests.cs`

- [ ] **Step 1: Napisz test**

`tests/LidarViewer.Core.Tests/PointAttributeTests.cs`:
```csharp
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class PointAttributeTests
{
    [Fact]
    public void Constructor_ComputesMinAndMax()
    {
        var attr = new PointAttribute("intensity", [5f, 1f, 9f]);

        Assert.Equal("intensity", attr.Name);
        Assert.Equal(1f, attr.Min);
        Assert.Equal(9f, attr.Max);
    }

    [Fact]
    public void Constructor_IgnoresNaN()
    {
        var attr = new PointAttribute("distance", [float.NaN, 2f, 4f]);

        Assert.Equal(2f, attr.Min);
        Assert.Equal(4f, attr.Max);
    }

    [Fact]
    public void Constructor_EmptyArray_GivesZeroRange()
    {
        var attr = new PointAttribute("height", []);

        Assert.Equal(0f, attr.Min);
        Assert.Equal(0f, attr.Max);
    }

    [Fact]
    public void Constructor_NullValues_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PointAttribute("x", null!));
    }
}
```

- [ ] **Step 2: Uruchom test — ma paść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~PointAttributeTests"`
Expected: FAIL — `LidarViewer.Core.Models.PointAttribute` nie istnieje (błąd kompilacji CS0246).

- [ ] **Step 3: Implementacja**

`src/LidarViewer.Core/Models/PointAttribute.cs`:
```csharp
namespace LidarViewer.Core.Models;

public sealed class PointAttribute
{
    public string Name { get; }

    public float[] Values { get; }

    public float Min { get; }

    public float Max { get; }

    public PointAttribute(string name, float[] values)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(values);

        Name = name;
        Values = values;

        if (values.Length == 0)
        {
            Min = 0f;
            Max = 0f;
            return;
        }

        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float value in values)
        {
            if (float.IsNaN(value)) continue;
            if (value < min) min = value;
            if (value > max) max = value;
        }

        Min = min == float.MaxValue ? 0f : min;
        Max = max == float.MinValue ? 0f : max;
    }
}
```

`src/LidarViewer.Core/Models/ScalarKind.cs`:
```csharp
namespace LidarViewer.Core.Models;

public enum ScalarKind
{
    Intensity,
    Distance,
    Height,
}
```

- [ ] **Step 4: Uruchom test — ma przejść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~PointAttributeTests"`
Expected: PASS — 4 testy.

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.Core/Models/PointAttribute.cs src/LidarViewer.Core/Models/ScalarKind.cs tests/LidarViewer.Core.Tests/PointAttributeTests.cs
git commit -m "feat(core): atrybut punktow z zakresem wartosci"
```

---

### Task 2: `PointCloud.GetAttribute` (intensywność, odległość, wysokość)

**Files:**
- Modify: `src/LidarViewer.Core/Models/PointCloud.cs`
- Test: `tests/LidarViewer.Core.Tests/ScalarFieldsTests.cs`

- [ ] **Step 1: Napisz test**

`tests/LidarViewer.Core.Tests/ScalarFieldsTests.cs`:
```csharp
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Tests;

public class ScalarFieldsTests
{
    [Fact]
    public void GetAttribute_Intensity_ConvertsBytesToFloats()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f], [10, 250]);

        var attr = cloud.GetAttribute(ScalarKind.Intensity);

        Assert.NotNull(attr);
        Assert.Equal("intensity", attr.Name);
        Assert.Equal([10f, 250f], attr.Values);
        Assert.Equal(10f, attr.Min);
        Assert.Equal(250f, attr.Max);
    }

    [Fact]
    public void GetAttribute_Intensity_ReturnsNullWhenAbsent()
    {
        var cloud = new PointCloud([0f, 0f, 0f]);

        Assert.Null(cloud.GetAttribute(ScalarKind.Intensity));
    }

    [Fact]
    public void GetAttribute_Distance_UsesOriginShiftedPositions()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 3f, 4f, 0f]) { Origin = (10, 0, 0) };

        var attr = cloud.GetAttribute(ScalarKind.Distance);

        Assert.NotNull(attr);
        Assert.Equal("distance", attr.Name);
        Assert.Equal(10f, attr.Values[0], 4);
        Assert.Equal(MathF.Sqrt(185f), attr.Values[1], 4);
    }

    [Fact]
    public void GetAttribute_Height_ReturnsZCoordinate()
    {
        var cloud = new PointCloud([1f, 2f, 3f, 4f, 5f, -6f]);

        var attr = cloud.GetAttribute(ScalarKind.Height);

        Assert.NotNull(attr);
        Assert.Equal("height", attr.Name);
        Assert.Equal([3f, -6f], attr.Values);
        Assert.Equal(-6f, attr.Min);
        Assert.Equal(3f, attr.Max);
    }

    [Fact]
    public void GetAttribute_ReturnsSameInstanceOnSecondCall()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 0f, 0f]);

        var first = cloud.GetAttribute(ScalarKind.Distance);
        var second = cloud.GetAttribute(ScalarKind.Distance);

        Assert.Same(first, second);
    }
}
```

- [ ] **Step 2: Uruchom test — ma paść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~ScalarFieldsTests"`
Expected: FAIL — brak metody `GetAttribute` (CS1061).

- [ ] **Step 3: Implementacja**

W `src/LidarViewer.Core/Models/PointCloud.cs` dodaj do klasy `PointCloud` (przed `WithoutInvalidPoints`):

```csharp
    private readonly Dictionary<ScalarKind, PointAttribute> _attributeCache = [];

    public PointAttribute? GetAttribute(ScalarKind kind)
    {
        if (_attributeCache.TryGetValue(kind, out var cached))
            return cached;

        PointAttribute? computed = kind switch
        {
            ScalarKind.Intensity => BuildIntensityAttribute(),
            ScalarKind.Distance => BuildDistanceAttribute(),
            ScalarKind.Height => BuildHeightAttribute(),
            _ => null,
        };

        if (computed is not null)
            _attributeCache[kind] = computed;

        return computed;
    }

    private PointAttribute? BuildIntensityAttribute()
    {
        if (Intensity is null) return null;

        var values = new float[Intensity.Length];
        for (int i = 0; i < Intensity.Length; i++)
            values[i] = Intensity[i];

        return new PointAttribute("intensity", values);
    }

    private PointAttribute BuildDistanceAttribute()
    {
        var values = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            float x = Positions[i * 3] + (float)Origin.X;
            float y = Positions[i * 3 + 1] + (float)Origin.Y;
            float z = Positions[i * 3 + 2] + (float)Origin.Z;
            values[i] = MathF.Sqrt(x * x + y * y + z * z);
        }

        return new PointAttribute("distance", values);
    }

    private PointAttribute BuildHeightAttribute()
    {
        var values = new float[Count];
        for (int i = 0; i < Count; i++)
            values[i] = Positions[i * 3 + 2];

        return new PointAttribute("height", values);
    }
```

- [ ] **Step 4: Uruchom test — ma przejść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~ScalarFieldsTests"`
Expected: PASS — 5 testów.

- [ ] **Step 5: Uruchom cały zestaw testów Core**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj`
Expected: PASS — wszystkie dotychczasowe testy nadal zielone.

- [ ] **Step 6: Commit**

```bash
git add src/LidarViewer.Core/Models/PointCloud.cs tests/LidarViewer.Core.Tests/ScalarFieldsTests.cs
git commit -m "feat(core): skalary intensity, distance i height dla chmury"
```

---

### Task 3: Mapy kolorów

**Files:**
- Create: `src/LidarViewer.Core/Rendering/Rgb.cs`
- Create: `src/LidarViewer.Core/Rendering/Rgba.cs`
- Create: `src/LidarViewer.Core/Rendering/ColormapKind.cs`
- Create: `src/LidarViewer.Core/Rendering/Colormaps.cs`
- Test: `tests/LidarViewer.Core.Tests/ColormapsTests.cs`

- [ ] **Step 1: Napisz test**

`tests/LidarViewer.Core.Tests/ColormapsTests.cs`:
```csharp
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class ColormapsTests
{
    [Fact]
    public void Build_ReturnsRequestedNumberOfColors()
    {
        var lut = Colormaps.Build(ColormapKind.Viridis, 64);

        Assert.Equal(64, lut.Length);
    }

    [Fact]
    public void Build_SizeBelowTwo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Colormaps.Build(ColormapKind.Jet, 1));
    }

    [Fact]
    public void Grayscale_StartsBlackAndEndsWhite()
    {
        var lut = Colormaps.Build(ColormapKind.Grayscale, 256);

        Assert.Equal(new Rgb(0, 0, 0), lut[0]);
        Assert.Equal(new Rgb(255, 255, 255), lut[^1]);
    }

    [Fact]
    public void Grayscale_IsMonotonicInBrightness()
    {
        var lut = Colormaps.Build(ColormapKind.Grayscale, 256);

        for (int i = 1; i < lut.Length; i++)
            Assert.True(lut[i].R >= lut[i - 1].R, $"przy indeksie {i}");
    }

    [Fact]
    public void Viridis_StartsDarkPurpleAndEndsYellow()
    {
        var lut = Colormaps.Build(ColormapKind.Viridis, 256);

        Assert.Equal(new Rgb(68, 1, 84), lut[0]);
        Assert.Equal(new Rgb(253, 231, 37), lut[^1]);
    }

    [Fact]
    public void Turbo_StartsDarkBlueAndEndsDarkRed()
    {
        var lut = Colormaps.Build(ColormapKind.Turbo, 256);

        Assert.True(lut[0].B > lut[0].R);
        Assert.True(lut[^1].R > lut[^1].B);
    }

    [Fact]
    public void Jet_StartsBlueAndEndsDarkRed()
    {
        var lut = Colormaps.Build(ColormapKind.Jet, 256);

        Assert.Equal(new Rgb(0, 0, 127), lut[0]);
        Assert.Equal(new Rgb(127, 0, 0), lut[^1]);
    }

    [Fact]
    public void All_ContainsEveryKind()
    {
        Assert.Equal(4, Colormaps.All.Count);
        Assert.Contains(ColormapKind.Grayscale, Colormaps.All);
        Assert.Contains(ColormapKind.Viridis, Colormaps.All);
        Assert.Contains(ColormapKind.Turbo, Colormaps.All);
        Assert.Contains(ColormapKind.Jet, Colormaps.All);
    }
}
```

- [ ] **Step 2: Uruchom test — ma paść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~ColormapsTests"`
Expected: FAIL — brak `LidarViewer.Core.Rendering.Colormaps` (CS0246).

- [ ] **Step 3: Implementacja**

`src/LidarViewer.Core/Rendering/Rgb.cs`:
```csharp
namespace LidarViewer.Core.Rendering;

public readonly record struct Rgb(byte R, byte G, byte B);
```

`src/LidarViewer.Core/Rendering/Rgba.cs`:
```csharp
namespace LidarViewer.Core.Rendering;

public readonly record struct Rgba(byte R, byte G, byte B, byte A);
```

`src/LidarViewer.Core/Rendering/ColormapKind.cs`:
```csharp
namespace LidarViewer.Core.Rendering;

public enum ColormapKind
{
    Grayscale,
    Viridis,
    Turbo,
    Jet,
}
```

`src/LidarViewer.Core/Rendering/Colormaps.cs`:
```csharp
namespace LidarViewer.Core.Rendering;

public static class Colormaps
{
    public const int DefaultSize = 256;

    public static IReadOnlyList<ColormapKind> All { get; } =
    [
        ColormapKind.Grayscale,
        ColormapKind.Viridis,
        ColormapKind.Turbo,
        ColormapKind.Jet,
    ];

    private static readonly (float T, byte R, byte G, byte B)[] ViridisAnchors =
    [
        (0.00f, 68, 1, 84),
        (0.10f, 72, 40, 120),
        (0.20f, 62, 74, 137),
        (0.30f, 49, 104, 142),
        (0.40f, 38, 130, 142),
        (0.50f, 31, 158, 137),
        (0.60f, 53, 183, 121),
        (0.70f, 109, 205, 89),
        (0.80f, 180, 222, 44),
        (0.90f, 222, 227, 24),
        (1.00f, 253, 231, 37),
    ];

    private static readonly (float T, byte R, byte G, byte B)[] TurboAnchors =
    [
        (0.00f, 48, 18, 59),
        (0.10f, 70, 134, 251),
        (0.20f, 57, 181, 229),
        (0.30f, 38, 229, 185),
        (0.40f, 48, 246, 113),
        (0.50f, 116, 252, 92),
        (0.60f, 188, 222, 38),
        (0.70f, 227, 163, 41),
        (0.80f, 252, 100, 18),
        (0.90f, 223, 42, 10),
        (1.00f, 122, 4, 3),
    ];

    private static readonly (float T, byte R, byte G, byte B)[] JetAnchors =
    [
        (0.00f, 0, 0, 127),
        (0.125f, 0, 0, 255),
        (0.375f, 0, 255, 255),
        (0.625f, 255, 255, 0),
        (0.875f, 255, 0, 0),
        (1.00f, 127, 0, 0),
    ];

    public static Rgb[] Build(ColormapKind kind, int size = DefaultSize)
    {
        if (size < 2)
            throw new ArgumentOutOfRangeException(nameof(size), "Tablica kolorow musi miec co najmniej 2 elementy.");

        var lut = new Rgb[size];
        for (int i = 0; i < size; i++)
        {
            float t = (float)i / (size - 1);
            lut[i] = kind switch
            {
                ColormapKind.Viridis => FromAnchors(t, ViridisAnchors),
                ColormapKind.Turbo => FromAnchors(t, TurboAnchors),
                ColormapKind.Jet => FromAnchors(t, JetAnchors),
                _ => Grayscale(t),
            };
        }

        return lut;
    }

    private static Rgb Grayscale(float t)
    {
        byte v = (byte)Math.Clamp(MathF.Round(t * 255f), 0f, 255f);
        return new Rgb(v, v, v);
    }

    private static Rgb FromAnchors(float t, (float T, byte R, byte G, byte B)[] anchors)
    {
        if (t <= anchors[0].T)
            return new Rgb(anchors[0].R, anchors[0].G, anchors[0].B);

        for (int i = 1; i < anchors.Length; i++)
        {
            if (t > anchors[i].T) continue;

            var a = anchors[i - 1];
            var b = anchors[i];
            float span = b.T - a.T;
            float f = span <= 0f ? 0f : (t - a.T) / span;

            return new Rgb(
                (byte)Math.Clamp(MathF.Round(a.R + (b.R - a.R) * f), 0f, 255f),
                (byte)Math.Clamp(MathF.Round(a.G + (b.G - a.G) * f), 0f, 255f),
                (byte)Math.Clamp(MathF.Round(a.B + (b.B - a.B) * f), 0f, 255f));
        }

        var last = anchors[^1];
        return new Rgb(last.R, last.G, last.B);
    }
}
```

- [ ] **Step 4: Uruchom test — ma przejść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~ColormapsTests"`
Expected: PASS — 8 testów.

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.Core/Rendering/Rgb.cs src/LidarViewer.Core/Rendering/Rgba.cs src/LidarViewer.Core/Rendering/ColormapKind.cs src/LidarViewer.Core/Rendering/Colormaps.cs tests/LidarViewer.Core.Tests/ColormapsTests.cs
git commit -m "feat(core): mapy kolorow grayscale, viridis, turbo, jet"
```

---

### Task 4: `ColorRamp`, `ColorMode`, `ColorSettings`

**Files:**
- Create: `src/LidarViewer.Core/Rendering/ColorRamp.cs`
- Create: `src/LidarViewer.Core/Rendering/ColorMode.cs`
- Create: `src/LidarViewer.Core/Rendering/ColorSettings.cs`
- Test: `tests/LidarViewer.Core.Tests/ColorRampTests.cs`

- [ ] **Step 1: Napisz test**

`tests/LidarViewer.Core.Tests/ColorRampTests.cs`:
```csharp
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class ColorRampTests
{
    private static readonly Rgb First = new(0, 0, 0);
    private static readonly Rgb Last = new(255, 255, 255);

    [Fact]
    public void ColorAt_ValueBelowMin_ClampsToFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(First, ramp.ColorAt(0f));
    }

    [Fact]
    public void ColorAt_ValueAboveMax_ClampsToLastColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(Last, ramp.ColorAt(1000f));
    }

    [Fact]
    public void ColorAt_ValueAtMin_GivesFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(First, ramp.ColorAt(10f));
    }

    [Fact]
    public void ColorAt_ValueAtMax_GivesLastColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 10f, 20f);

        Assert.Equal(Last, ramp.ColorAt(20f));
    }

    [Fact]
    public void ColorAt_Midpoint_GivesMiddleBrightness()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 0f, 100f);

        var mid = ramp.ColorAt(50f);

        Assert.InRange(mid.R, 126, 130);
    }

    [Fact]
    public void ColorAt_EqualMinAndMax_ReturnsFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 5f, 5f);

        Assert.Equal(First, ramp.ColorAt(5f));
    }

    [Fact]
    public void ColorAt_NaN_ReturnsFirstColor()
    {
        var ramp = new ColorRamp(ColormapKind.Grayscale, 0f, 10f);

        Assert.Equal(First, ramp.ColorAt(float.NaN));
    }
}
```

- [ ] **Step 2: Uruchom test — ma paść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~ColorRampTests"`
Expected: FAIL — brak `ColorRamp` (CS0246).

- [ ] **Step 3: Implementacja**

`src/LidarViewer.Core/Rendering/ColorRamp.cs`:
```csharp
namespace LidarViewer.Core.Rendering;

public sealed class ColorRamp
{
    private readonly Rgb[] _lut;

    public ColormapKind Map { get; }

    public float Min { get; }

    public float Max { get; }

    public ColorRamp(ColormapKind map, float min, float max, int size = Colormaps.DefaultSize)
    {
        Map = map;
        Min = min;
        Max = max;
        _lut = Colormaps.Build(map, size);
    }

    public Rgb ColorAt(float value)
    {
        if (Max <= Min)
            return _lut[0];

        float t = (value - Min) / (Max - Min);
        if (float.IsNaN(t))
            return _lut[0];

        t = Math.Clamp(t, 0f, 1f);
        int index = (int)(t * (_lut.Length - 1) + 0.5f);
        return _lut[index];
    }
}
```

`src/LidarViewer.Core/Rendering/ColorMode.cs`:
```csharp
namespace LidarViewer.Core.Rendering;

public enum ColorMode
{
    Solid,
    Intensity,
    Distance,
}
```

`src/LidarViewer.Core/Rendering/ColorSettings.cs`:
```csharp
namespace LidarViewer.Core.Rendering;

public sealed record ColorSettings
{
    public static readonly ColorSettings Default = new();

    public ColorMode Mode { get; init; } = ColorMode.Solid;

    public ColormapKind Map { get; init; } = ColormapKind.Viridis;

    public float Min { get; init; }

    public float Max { get; init; } = 1f;

    public Rgba Solid { get; init; } = new(0x33, 0xE6, 0x4D, 0xFF);
}
```

- [ ] **Step 4: Uruchom test — ma przejść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~ColorRampTests"`
Expected: PASS — 7 testów.

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.Core/Rendering/ColorRamp.cs src/LidarViewer.Core/Rendering/ColorMode.cs src/LidarViewer.Core/Rendering/ColorSettings.cs tests/LidarViewer.Core.Tests/ColorRampTests.cs
git commit -m "feat(core): rampa kolorow z zakresem i ustawienia kolorowania"
```

---

### Task 5: `CloudColors` — budowa bufora RGBA

**Files:**
- Create: `src/LidarViewer.Core/Rendering/CloudColors.cs`
- Test: `tests/LidarViewer.Core.Tests/CloudColorsTests.cs`

- [ ] **Step 1: Napisz test**

`tests/LidarViewer.Core.Tests/CloudColorsTests.cs`:
```csharp
using LidarViewer.Core.Models;
using LidarViewer.Core.Rendering;

namespace LidarViewer.Core.Tests;

public class CloudColorsTests
{
    [Fact]
    public void Build_SolidMode_FillsEveryPointWithSameColor()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f]);

        var rgba = CloudColors.Build(cloud, new ColorSettings { Mode = ColorMode.Solid, Solid = new Rgba(10, 20, 30, 40) });

        Assert.Equal(8, rgba.Length);
        Assert.Equal([(byte)10, (byte)20, (byte)30, (byte)40], rgba[..4]);
        Assert.Equal([(byte)10, (byte)20, (byte)30, (byte)40], rgba[4..]);
    }

    [Fact]
    public void Build_IntensityMode_MapsExtremesToRampEnds()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f], [0, 255]);

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Intensity,
            Map = ColormapKind.Grayscale,
            Min = 0f,
            Max = 255f,
        });

        Assert.Equal((byte)0, rgba[0]);
        Assert.Equal((byte)255, rgba[4]);
        Assert.Equal((byte)255, rgba[5]);
        Assert.Equal((byte)255, rgba[6]);
    }

    [Fact]
    public void Build_DistanceMode_UsesRangeFromOrigin()
    {
        var cloud = new PointCloud([0f, 0f, 0f]) { Origin = (10, 0, 0) };

        var rgba = CloudColors.Build(cloud, new ColorSettings
        {
            Mode = ColorMode.Distance,
            Map = ColormapKind.Grayscale,
            Min = 0f,
            Max = 10f,
        });

        Assert.Equal((byte)255, rgba[0]);
    }

    [Fact]
    public void Build_MissingIntensity_FallsBackToGray()
    {
        var cloud = new PointCloud([0f, 0f, 0f, 1f, 1f, 1f]);

        var rgba = CloudColors.Build(cloud, new ColorSettings { Mode = ColorMode.Intensity });

        Assert.Equal((byte)128, rgba[0]);
        Assert.Equal((byte)128, rgba[1]);
        Assert.Equal((byte)128, rgba[2]);
        Assert.Equal((byte)255, rgba[3]);
    }

    [Fact]
    public void Build_EmptyCloud_ReturnsEmptyBuffer()
    {
        var rgba = CloudColors.Build(new PointCloud([]), new ColorSettings());

        Assert.Empty(rgba);
    }
}
```

- [ ] **Step 2: Uruchom test — ma paść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~CloudColorsTests"`
Expected: FAIL — brak `CloudColors` (CS0246).

- [ ] **Step 3: Implementacja**

`src/LidarViewer.Core/Rendering/CloudColors.cs`:
```csharp
using LidarViewer.Core.Models;

namespace LidarViewer.Core.Rendering;

public static class CloudColors
{
    private const byte FallbackGray = 128;

    public static byte[] Build(PointCloud cloud, ColorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(settings);

        int count = cloud.Count;
        var rgba = new byte[count * 4];

        if (settings.Mode == ColorMode.Solid)
        {
            for (int i = 0; i < count; i++)
            {
                rgba[i * 4] = settings.Solid.R;
                rgba[i * 4 + 1] = settings.Solid.G;
                rgba[i * 4 + 2] = settings.Solid.B;
                rgba[i * 4 + 3] = settings.Solid.A;
            }
            return rgba;
        }

        ScalarKind kind = settings.Mode == ColorMode.Intensity
            ? ScalarKind.Intensity
            : ScalarKind.Distance;

        var attribute = cloud.GetAttribute(kind);
        if (attribute is null)
        {
            for (int i = 0; i < count; i++)
            {
                rgba[i * 4] = FallbackGray;
                rgba[i * 4 + 1] = FallbackGray;
                rgba[i * 4 + 2] = FallbackGray;
                rgba[i * 4 + 3] = 0xFF;
            }
            return rgba;
        }

        var ramp = new ColorRamp(settings.Map, settings.Min, settings.Max);
        var values = attribute.Values;
        for (int i = 0; i < count; i++)
        {
            var color = ramp.ColorAt(values[i]);
            rgba[i * 4] = color.R;
            rgba[i * 4 + 1] = color.G;
            rgba[i * 4 + 2] = color.B;
            rgba[i * 4 + 3] = 0xFF;
        }

        return rgba;
    }
}
```

- [ ] **Step 4: Uruchom test — ma przejść**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --filter "FullyQualifiedName~CloudColorsTests"`
Expected: PASS — 5 testów.

- [ ] **Step 5: Uruchom cały zestaw testów**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj`
Expected: PASS — wszystkie testy Core zielone.

- [ ] **Step 6: Commit**

```bash
git add src/LidarViewer.Core/Rendering/CloudColors.cs tests/LidarViewer.Core.Tests/CloudColorsTests.cs
git commit -m "feat(core): bufor kolorow RGBA dla chmury punktow"
```

---

### Task 6: Render 3D z kolorem per punkt

**Files:**
- Modify: `src/LidarViewer.App/Controls/PointCloudView3D.cs`

**Uwaga:** brak testów jednostkowych — logika GPU. Weryfikacja: build + Task 10 (ręczna).

- [ ] **Step 1: Zmień shadery**

W `PointCloudView3D.cs` zamień stałe `VertexShader` i `FragmentShader` na:

```csharp
    private const string VertexShader = """
        in vec3 aPos;
        in vec4 aColor;
        uniform mat4 uMvp;
        uniform float uPointSize;
        out vec4 vColor;
        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            gl_PointSize = uPointSize;
            vColor = aColor;
        }
        """;

    private const string FragmentShader = """
        in vec4 vColor;
        out vec4 FragColor;
        void main()
        {
            FragColor = vColor;
        }
        """;
```

- [ ] **Step 2: Dodaj właściwość `ColorSettings`**

W `PointCloudView3D.cs` dodaj po bloku `Diagnostics`:

```csharp
    public static readonly StyledProperty<ColorSettings?> ColorSettingsProperty =
        AvaloniaProperty.Register<PointCloudView3D, ColorSettings?>(nameof(ColorSettings));

    public ColorSettings? ColorSettings
    {
        get => GetValue(ColorSettingsProperty);
        set => SetValue(ColorSettingsProperty, value);
    }
```

oraz w konstruktorze statycznym dopisz handler:

```csharp
        ColorSettingsProperty.Changed.AddClassHandler<PointCloudView3D>((view, _) => view.InvalidateColors());
```

i handler istniejący dla `PointCloudProperty` zmień na:

```csharp
        PointCloudProperty.Changed.AddClassHandler<PointCloudView3D>((view, e) =>
        {
            view._cloud = e.NewValue as PointCloud;
            view._uploadPending = true;
            view.InvalidateColors();
            view.RequestNextFrameRendering();
        });
```

- [ ] **Step 3: Dodaj pola buforów**

W `PointCloudView3D.cs` zamień deklarację pól buforów:

```csharp
    private GL? _gl;
    private bool _initOk;
    private uint _program, _vao, _vbo, _colVbo;
    private int _mvpLocation, _pointSizeLocation;
```

W tym fragmencie usuń `_colorLocation` — uniform koloru zniknął.

Dodaj nowe pola:

```csharp
    private bool _colorsDirty = true;
    private byte[] _colorBuffer = [];
```

- [ ] **Step 4: Utwórz VBO kolorów i atrybut w VAO**

W `InitGl`, po linii `_gl.EnableVertexAttribArray(0);` dodaj:

```csharp
        _colVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _colVbo);
        unsafe { _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, 4, null); }
        _gl.EnableVertexAttribArray(1);
```

- [ ] **Step 5: Wgraj kolory**

W `PointCloudView3D.cs` dodaj metodę (przed `UploadPoints`):

```csharp
    private void InvalidateColors()
    {
        _colorsDirty = true;
        RequestNextFrameRendering();
    }

    private unsafe void UploadColors(GL gl)
    {
        _colorsDirty = false;

        var cloud = _cloud;
        if (cloud is null || cloud.Count == 0)
        {
            _colorBuffer = [];
            return;
        }

        byte[] rgba = CloudColors.Build(cloud, ColorSettings ?? ColorSettings.Default);
        _colorBuffer = rgba;

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _colVbo);
        fixed (byte* data = rgba)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)rgba.Length, data, BufferUsageARB.DynamicDraw);
    }
```

- [ ] **Step 6: Podepnij w pętli renderowania**

W `RenderFrame`, po `if (_uploadPending) UploadPoints(gl);` dodaj:

```csharp
        if (_colorsDirty) UploadColors(gl);
```

Usuń z `RenderFrame` linię:
```csharp
            gl.Uniform4(_colorLocation, 0.2f, 0.9f, 0.3f, 1f);
```

- [ ] **Step 7: Zwolnij VBO kolorów**

W `OnOpenGlDeinit`, po `_gl.DeleteBuffer(_vbo);` dodaj:

```csharp
        _gl.DeleteBuffer(_colVbo);
```

- [ ] **Step 8: Dodaj `using` dla Core.Rendering**

Upewnij się, że na górze `PointCloudView3D.cs` jest:
```csharp
using LidarViewer.Core.Rendering;
```
(istnieje już — `OrbitCamera` jest z tej przestrzeni nazw, więc nic nie zmieniaj, jeśli jest).

- [ ] **Step 9: Build**

Run: `dotnet build LidarViewer.slnx -v q --nologo`
Expected: Build succeeded, 0 errors. Upewnij się, że nie ma odwołania do `_colorLocation`.

- [ ] **Step 10: Commit**

```bash
git add src/LidarViewer.App/Controls/PointCloudView3D.cs
git commit -m "feat(app): render 3D z kolorem per punkt"
```

---

### Task 7: Render 2D z kolorem per punkt

**Files:**
- Modify: `src/LidarViewer.App/Controls/PointCloudView.cs`

- [ ] **Step 1: Dodaj właściwość `ColorSettings`**

W `PointCloudView.cs` dodaj po bloku `Plane`:

```csharp
    public static readonly StyledProperty<ColorSettings?> ColorSettingsProperty =
        AvaloniaProperty.Register<PointCloudView, ColorSettings?>(nameof(ColorSettings));

    public ColorSettings? ColorSettings
    {
        get => GetValue(ColorSettingsProperty);
        set => SetValue(ColorSettingsProperty, value);
    }
```

oraz w konstruktorze statycznym dopisz handler:

```csharp
        ColorSettingsProperty.Changed.AddClassHandler<PointCloudView>((view, _) => view.InvalidateColors());
```

- [ ] **Step 2: Dodaj cache kolorów**

W `PointCloudView.cs` dodaj pola:

```csharp
    private byte[] _colorBuffer = [];
    private bool _colorsDirty = true;
```

i metodę:

```csharp
    private void InvalidateColors()
    {
        _colorsDirty = true;
        InvalidateVisual();
    }
```

- [ ] **Step 3: Buduj kolory w `Rasterize`**

W `PointCloudView.cs` w metodzie `Rasterize`, po `if (PointCloud is not { Count: > 0 } cloud) return;` dodaj:

```csharp
        if (_colorsDirty)
        {
            _colorBuffer = CloudColors.Build(cloud, ColorSettings ?? ColorSettings.Default);
            _colorsDirty = false;
        }
```

- [ ] **Step 4: Rysuj kolorowe punkty**

W `Rasterize` zamień pętlę po punktach:

```csharp
        var p = cloud.Positions;
        for (int i = 0; i < p.Length; i += 3)
        {
            int pointIndex = i / 3;
            int x = (int)((p[i + ha] - centerX) * scale + offsetX);
            int y = (int)(offsetY - (p[i + va] - centerY) * scale);
            int color = ToBgra(pointIndex);
            for (int dy = 0; dy < 2; dy++)
            {
                int yy = y + dy;
                if ((uint)yy >= (uint)height) continue;
                for (int dx = 0; dx < 2; dx++)
                {
                    int xx = x + dx;
                    if ((uint)xx < (uint)width)
                        _pixels[yy * width + xx] = color;
                }
            }
        }
```

i dodaj metodę pomocniczą:

```csharp
    private int ToBgra(int pointIndex)
    {
        int o = pointIndex * 4;
        if (o + 3 >= _colorBuffer.Length)
            return unchecked((int)0xFF808080);

        return _colorBuffer[o]
               | (_colorBuffer[o + 1] << 8)
               | (_colorBuffer[o + 2] << 16)
               | unchecked((int)0xFF000000);
    }
```

- [ ] **Step 5: Dodaj `using`**

Upewnij się, że `PointCloudView.cs` ma:
```csharp
using LidarViewer.Core.Rendering;
```

- [ ] **Step 6: Build**

Run: `dotnet build LidarViewer.slnx -v q --nologo`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/LidarViewer.App/Controls/PointCloudView.cs
git commit -m "feat(app): render 2D z kolorem per punkt"
```

---

### Task 8: Stan kolorów w `MainViewModel`

**Files:**
- Modify: `src/LidarViewer.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Dodaj właściwości i importy**

W `MainViewModel.cs` dodaj na górze:
```csharp
using LidarViewer.Core.Rendering;
```

a w klasie po `public partial string? Diagnostics { get; set; }` dodaj:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    [NotifyPropertyChangedFor(nameof(LegendTitle))]
    [NotifyPropertyChangedFor(nameof(ShowLegend))]
    public partial ColorMode ColorMode { get; set; } = ColorMode.Solid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    public partial ColormapKind Colormap { get; set; } = ColormapKind.Viridis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    public partial bool AutoColorRange { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    public partial float ColorRangeMin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorSettings))]
    public partial float ColorRangeMax { get; set; } = 1f;

    public ColorMode[] ColorModes { get; } = Enum.GetValues<ColorMode>();

    public ColormapKind[] Colormaps { get; } = Enum.GetValues<ColormapKind>();

    public ColorSettings ColorSettings => new()
    {
        Mode = ColorMode,
        Map = Colormap,
        Min = ColorRangeMin,
        Max = ColorRangeMax,
    };

    public bool ShowLegend => ColorMode != ColorMode.Solid;

    public string LegendTitle => ColorMode switch
    {
        ColorMode.Intensity => "Intensywnosc",
        ColorMode.Distance => "Range (odleglosc od sensora)",
        _ => "Jednolity",
    };
```

- [ ] **Step 2: Dodaj automatyczne dopasowanie zakresu**

W `MainViewModel.cs` dodaj metody:

```csharp
    partial void OnColorModeChanged(ColorMode value) => ApplyAutoRange();

    partial void OnAutoColorRangeChanged(bool value) => ApplyAutoRange();

    private void ApplyAutoRange()
    {
        if (!AutoColorRange || PointCloud is null)
            return;

        ScalarKind kind = ColorMode == ColorMode.Intensity
            ? ScalarKind.Intensity
            : ScalarKind.Distance;

        var attribute = PointCloud.GetAttribute(kind);
        if (attribute is null)
            return;

        ColorRangeMin = attribute.Min;
        ColorRangeMax = attribute.Max;
    }
```

oraz `using LidarViewer.Core.Models;` (jest już).

- [ ] **Step 3: Wywołaj `ApplyAutoRange` po wczytaniu pliku**

W metodzie `LoadAsync`, po linii `PointCloud = cloud;` dodaj:

```csharp
            ApplyAutoRange();
```

- [ ] **Step 4: Build**

Run: `dotnet build LidarViewer.slnx -v q --nologo`
Expected: Build succeeded, 0 errors. Upewnij się, że CommunityToolkit wygenerował `OnColorModeChanged` i `OnAutoColorRangeChanged` (metody `partial void` bez implementacji kompilują się bez błędu).

- [ ] **Step 5: Commit**

```bash
git add src/LidarViewer.App/ViewModels/MainViewModel.cs
git commit -m "feat(app): stan kolorowania i automatyczny zakres w view modelu"
```

---

### Task 9: Kontrolka legendy

**Files:**
- Create: `src/LidarViewer.App/Controls/ColorLegend.cs`

- [ ] **Step 1: Utwórz kontrolkę**

`src/LidarViewer.App/Controls/ColorLegend.cs`:
```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LidarViewer.Core.Rendering;

namespace LidarViewer.App.Controls;

public sealed class ColorLegend : Panel
{
    private const double BarHeight = 14;
    private const int StopsPerMap = 16;

    private readonly Grid _root;
    private readonly TextBlock _title = new() { FontSize = 11, Foreground = Brushes.White };
    private readonly Border _bar = new()
    {
        Height = BarHeight,
        BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
        BorderThickness = new Thickness(1),
    };
    private readonly TextBlock _minLabel = new() { FontSize = 10, Foreground = Brushes.LightGray };
    private readonly TextBlock _maxLabel = new()
    {
        FontSize = 10,
        Foreground = Brushes.LightGray,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    public static readonly StyledProperty<ColorSettings?> ColorSettingsProperty =
        AvaloniaProperty.Register<ColorLegend, ColorSettings?>(nameof(ColorSettings));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ColorLegend, string?>(nameof(Title));

    public ColorSettings? ColorSettings
    {
        get => GetValue(ColorSettingsProperty);
        set => SetValue(ColorSettingsProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    static ColorLegend()
    {
        ColorSettingsProperty.Changed.AddClassHandler<ColorLegend>((view, _) => view.Refresh());
    }

    public ColorLegend()
    {
        _root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
        };

        Grid.SetRow(_title, 0);
        Grid.SetColumnSpan(_title, 2);
        Grid.SetRow(_bar, 1);
        Grid.SetColumnSpan(_bar, 2);
        Grid.SetRow(_minLabel, 2);
        Grid.SetColumn(_minLabel, 0);
        Grid.SetRow(_maxLabel, 2);
        Grid.SetColumn(_maxLabel, 1);

        _root.Children.Add(_title);
        _root.Children.Add(_bar);
        _root.Children.Add(_minLabel);
        _root.Children.Add(_maxLabel);

        Children.Add(_root);
        Width = 280;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _root.Measure(new Size(Width, availableSize.Height));
        return new Size(Width, _root.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void Refresh()
    {
        var settings = ColorSettings;
        if (settings is null)
            return;

        _title.Text = Title ?? string.Empty;
        _bar.Background = BuildBrush(settings.Map);
        _minLabel.Text = Format(settings.Min);
        _maxLabel.Text = Format(settings.Max);
    }

    private static IBrush BuildBrush(ColormapKind map)
    {
        var lut = Colormaps.Build(map);
        var stops = new GradientStopCollection();

        for (int i = 0; i < StopsPerMap; i++)
        {
            float t = (float)i / (StopsPerMap - 1);
            var color = lut[(int)(t * (lut.Length - 1) + 0.5f)];
            stops.Add(new GradientStop(Color.FromRgb(color.R, color.G, color.B), t));
        }

        return new LinearGradientBrush
        {
            GradientStops = stops,
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };
    }

    private static string Format(float value) =>
        value.ToString("0.###", CultureInfo.CurrentCulture);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build LidarViewer.slnx -v q --nologo`
Expected: Build succeeded, 0 errors.

Jeśli `Avalonia.Layout` nie jest potrzebny — usuń ten `using`. Jeśli kompilator zgłosi, że `RelativePoint` jest w `Avalonia` — jest w `Avalonia` namespace, więc `using Avalonia;` wystarczy.

- [ ] **Step 3: Commit**

```bash
git add src/LidarViewer.App/Controls/ColorLegend.cs
git commit -m "feat(app): legenda kolorow z gradientem"
```

---

### Task 10: Okno — kontrolki kolorów i legenda

**Files:**
- Modify: `src/LidarViewer.App/Views/MainWindow.axaml`

- [ ] **Step 1: Podziel toolbar na dwa panele**

W `MainWindow.axaml` zamień blok:
```xml
        <DockPanel DockPanel.Dock="Top">
            <Button DockPanel.Dock="Right" Content="Wyświetl" Command="{Binding LoadCommand}" />
            ...
        </DockPanel>
```
na:
```xml
        <StackPanel DockPanel.Dock="Top">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="Wyświetl" Command="{Binding LoadCommand}" />
                <ComboBox DockPanel.Dock="Right" ItemsSource="{Binding ViewModes}" SelectedItem="{Binding ViewMode}" />
                <ComboBox DockPanel.Dock="Right" ItemsSource="{Binding Planes}" SelectedItem="{Binding Plane}"
                          IsVisible="{Binding Is2D}" />
                <Button DockPanel.Dock="Right" Content="Wybierz plik..." Click="OnBrowseClick" />
                <TextBox Text="{Binding FilePath}" PlaceholderText="Ścieżka do pliku (.vtp, .vtk, .csv, .ply, .pcd, .las)" />
            </DockPanel>
            <DockPanel>
                <TextBlock DockPanel.Dock="Right" Text="Kolor:" VerticalAlignment="Center" Margin="0,0,4,0" />
                <ComboBox DockPanel.Dock="Right" ItemsSource="{Binding ColorModes}" SelectedItem="{Binding ColorMode}"
                          Width="120" />
                <TextBlock DockPanel.Dock="Right" Text="Mapa:" VerticalAlignment="Center" Margin="12,0,4,0"
                           IsVisible="{Binding ShowLegend}" />
                <ComboBox DockPanel.Dock="Right" ItemsSource="{Binding Colormaps}" SelectedItem="{Binding Colormap}"
                          Width="120" IsVisible="{Binding ShowLegend}" />
                <CheckBox DockPanel.Dock="Right" Content="Auto zakres" IsChecked="{Binding AutoColorRange}"
                          VerticalAlignment="Center" Margin="12,0,0,0" IsVisible="{Binding ShowLegend}" />
                <TextBox DockPanel.Dock="Right" Text="{Binding ColorRangeMin}" Width="80" Margin="8,0,0,0"
                         IsVisible="{Binding ShowLegend}" />
                <TextBox DockPanel.Dock="Right" Text="{Binding ColorRangeMax}" Width="80" Margin="4,0,12,0"
                         IsVisible="{Binding ShowLegend}" />
                <TextBlock DockPanel.Dock="Right" Text="min / max" VerticalAlignment="Center" Margin="0,0,4,0"
                           IsVisible="{Binding ShowLegend}" />
            </DockPanel>
        </StackPanel>
```

- [ ] **Step 2: Podepnij kolory do widoków i dodaj legendę**

W `MainWindow.axaml` zamień blok:
```xml
        <Grid>
            <c:PointCloudView3D IsVisible="{Binding Is3D}" PointCloud="{Binding PointCloud}"
                                Diagnostics="{Binding Diagnostics, Mode=OneWayToSource}" />
            <c:PointCloudView IsVisible="{Binding Is2D}" PointCloud="{Binding PointCloud}" Plane="{Binding Plane}" />
        </Grid>
```
na:
```xml
        <Grid>
            <c:PointCloudView3D IsVisible="{Binding Is3D}" PointCloud="{Binding PointCloud}"
                                ColorSettings="{Binding ColorSettings}"
                                Diagnostics="{Binding Diagnostics, Mode=OneWayToSource}" />
            <c:PointCloudView IsVisible="{Binding Is2D}" PointCloud="{Binding PointCloud}" Plane="{Binding Plane}"
                              ColorSettings="{Binding ColorSettings}" />
            <Border Grid.Align="Bottom" HorizontalAlignment="Left" Margin="12" Padding="8"
                    Background="#D91A1A1A" CornerRadius="4" IsVisible="{Binding ShowLegend}">
                <c:ColorLegend ColorSettings="{Binding ColorSettings}" Title="{Binding LegendTitle}" />
            </Border>
        </Grid>
```

- [ ] **Step 3: Build i uruchom**

Run: `dotnet build LidarViewer.slnx -v q --nologo`
Expected: Build succeeded, 0 errors.

Run: `dotnet run --project src/LidarViewer.App/LidarViewer.App.csproj`
Expected: okno otwiera się bez wyjątków, konsola nie zgłasza błędów Avalonia.

- [ ] **Step 4: Commit**

```bash
git add src/LidarViewer.App/Views/MainWindow.axaml
git commit -m "feat(app): kontrolki kolorowania i legenda w oknie"
```

---

### Task 11: Weryfikacja końcowa etapu 1

**Files:** brak zmian kodu (chyba że testy ujawnią błąd)

- [ ] **Step 1: Pełny build**

Run: `dotnet build LidarViewer.slnx -v q --nologo`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Pełny zestaw testów**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj`
Expected: PASS — wszystkie testy zielone (dotychczasowe + 29 nowych).

- [ ] **Step 3: Sprawdź pokrycie**

Run: `dotnet test tests/LidarViewer.Core.Tests/LidarViewer.Core.Tests.csproj --collect:"XPlat Code Coverage"`
Expected: pokrycie Core >= 80%.

- [ ] **Step 4: Ręczna lista kontrolna na plikach próbnych**

Uruchom aplikację i sprawdź na `lidar-samples/pdal_autzen_trim.las` (110 000 pkt, intensywność obecna):

1. Wczytanie pliku → punkty widoczne w 3D, domyślnie zielone (tryb Jednolity).
2. Przełączenie „Kolor" na **Intensywnosc** → chmura przechodzi na gradient szarości; legenda pokazuje pasek gradientu i zakres 0–255.
3. Przełączenie „Kolor" na **Range** → gradient wg odległości; wartości w pasku statusu rzędu 0–60 m dla tego pliku.
4. Zmiana mapy na **Viridis** → gradient fiolet→żółty; zmiana na **Turbo** → gradient niebieski→czerwony.
5. Odznaczenie „Auto zakres" i wpisanie ręcznego min/max → gradient się zmienia, wartości poza zakresem dostają kolor skrajny.
6. Przełączenie w widok 2D → te same kolory co w 3D.
7. Wczytanie `lidar-samples/pcl_lamppost.pcd` → pozycje w 3D, brak intensywności w pliku (sprawdź, czy opcja Intensywność jest sensowna; przy braku danych punkty są szare).

- [ ] **Step 5: Wydajność**

Wczytaj `lidar-samples/pointCloud.vtp` (11 MB) i przełącz tryb kolorowania. Chmura nie może zamrażać UI na dłużej niż ~1 s. Jeśli zamraża, zgłoś to w opisie commita — to sygnał do optymalizacji w etapie 3.

- [ ] **Step 6: Commit finalny etapu (jeśli były poprawki)**

```bash
git add -A
git commit -m "fix(app): poprawki po recenzji etapu 1"
```

Jeśli nie było poprawek — nie twórz pustego commita.

---

## Podsumowanie plików etapu 1

```
src/LidarViewer.Core/Models/PointAttribute.cs          NOWY
src/LidarViewer.Core/Models/ScalarKind.cs              NOWY
src/LidarViewer.Core/Models/PointCloud.cs               ZMIENIONY
src/LidarViewer.Core/Rendering/Rgb.cs                   NOWY
src/LidarViewer.Core/Rendering/Rgba.cs                  NOWY
src/LidarViewer.Core/Rendering/ColormapKind.cs          NOWY
src/LidarViewer.Core/Rendering/Colormaps.cs             NOWY
src/LidarViewer.Core/Rendering/ColorRamp.cs             NOWY
src/LidarViewer.Core/Rendering/ColorMode.cs             NOWY
src/LidarViewer.Core/Rendering/ColorSettings.cs         NOWY
src/LidarViewer.Core/Rendering/CloudColors.cs           NOWY
src/LidarViewer.App/Controls/PointCloudView3D.cs        ZMIENIONY
src/LidarViewer.App/Controls/PointCloudView.cs          ZMIENIONY
src/LidarViewer.App/Controls/ColorLegend.cs             NOWY
src/LidarViewer.App/ViewModels/MainViewModel.cs         ZMIENIONY
src/LidarViewer.App/Views/MainWindow.axaml              ZMIENIONY
tests/LidarViewer.Core.Tests/PointAttributeTests.cs     NOWY
tests/LidarViewer.Core.Tests/ScalarFieldsTests.cs       NOWY
tests/LidarViewer.Core.Tests/ColormapsTests.cs          NOWY
tests/LidarViewer.Core.Tests/ColorRampTests.cs          NOWY
tests/LidarViewer.Core.Tests/CloudColorsTests.cs        NOWY
```
