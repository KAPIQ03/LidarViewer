# Etap 3 — Sekwencje chmur punktów (odtwarzanie „video"): specyfikacja

**Cel:** aplikacja potrafi odtworzyć sekwencję chmur punktów jak film — z osią czasu,
play/pause, przewijaniem i tempem — a format na dysku jest tym samym, który
w Etapie 6 zapisze nagrywanie.

**Zakres:** odczyt sekwencji z katalogu, model odtwarzacza w Core, integracja z
`MainViewModel` i pasek sterowania w oknie.

**Poza zakresem (później):** interpolacja między klatkami, akumulacja historii,
nagrywanie (Etap 6), strumień live (Etap 4), wiele sekwencji naraz.

---

## 1. Format na dysku

Sekwencja to katalog z plikiem `sequence.json` i plikami klatek:

```
moj-skan/
  sequence.json
  frame_00000.las
  frame_00001.las
  ...
```

`sequence.json`:

```json
{
  "name": "Korytarz",
  "frames": [
    { "file": "frame_00000.las", "timestamp": 0.0 },
    { "file": "frame_00001.las", "timestamp": 0.1 }
  ]
}
```

- `name` — opis pokazywany w UI; brak → nazwa katalogu.
- `frames` — lista klatek; `file` to ścieżka względna wobec katalogu sekwencji.
- `timestamp` — czas klatki w sekundach od początku sekwencji; pole opcjonalne.
  Bez niego klatka dostaje czas `indeks * 0.1` (10 kl/s).
- Format klatek: dowolny obsługiwany przez `PointCloudLoader` (`.las`, `.pcd`,
  `.ply`, `.vtp`, `.vtk`, `.csv`).

**Dlaczego katalog + manifest, a nie jeden plik:** istniejące czytniki
pracują bez zmian, katalog z przykładowymi plikami staje się sekwencją od razu,
a manifest jest czytelny dla człowieka. Etap 6 zapisze dokładnie ten format.

**Reguły walidacji** (błąd → komunikat po polsku, brak wyjątku surowego):
- brak `sequence.json` w katalogu → „W katalogu nie ma pliku sequence.json."
- pusta lista klatek → „Sekwencja nie zawiera żadnej klatki."
- plik klatki nie istnieje → „Brak pliku klatki: {ścieżka}."
- `timestamp` ujemny lub mniejszy od poprzedniej klatki → „Casy klatek muszą rosnąć."

## 2. Core — model sekwencji

Nowy namespace `LidarViewer.Core.Playback`.

```csharp
public sealed record SequenceFrame(string File, double? Timestamp);

public sealed record SequenceManifest(string? Name, IReadOnlyList<SequenceFrame> Frames);

public sealed class Sequence
{
    public string Directory { get; }
    public string Name { get; }
    public IReadOnlyList<SequenceFrame> Frames { get; }
    public IReadOnlyList<double> Times { get; }   // efektywne czasy klatek (sekundy)
    public double Duration { get; }              // czas ostatniej klatki
    public PointCloud LoadFrame(int index);
}

public static class SequenceLoader
{
    public static Sequence Load(string directory);
}
```

- `SequenceLoader.Load` czyta `sequence.json` (`System.Text.Json`), parsuje
  manifest i waliduje katalog + czasy. Błędne dane → `InvalidDataException`
  z komunikatem po polsku.
- `Sequence.LoadFrame(index)` wczytuje plik klatki przez `PointCloudLoader`
  i pamięta **ostatnią klatkę** (cache jednej pozycji). Odtwarzanie czyta
  kolejne klatki, wiec cache wystarcza; przy przewijaniu wstecz plik wczytuje
  się ponownie.
- `Times` to czasy efektywne: podane w manifeście albo `indeks * 0.1`.

## 3. Core — odtwarzacz

```csharp
public sealed class SequencePlayer
{
    public SequencePlayer(Sequence sequence);
    public Sequence Sequence { get; }
    public int FrameIndex { get; }
    public double PositionSeconds { get; }
    public bool IsPlaying { get; }
    public double Speed { get; set; }      // 0.25 – 4.0
    public bool Loop { get; set; }
    public event Action<int>? FrameChanged;
    public void Play();
    public void Pause();
    public void Seek(double seconds);
    public void Advance(TimeSpan delta);
}
```

- `Advance(delta)` to jedyne wejście czasu — wywoływane z timera UI. Przy
  `IsPlaying == false` nic się nie dzieje.
- Pozycja to czas w sekundach; klatka wyznacza przedział
  `[Times[i], Times[i+1])`, ostatnia klatka trwa do końca sekwencji.
- `Speed` mnoży upływający czas; wartość poza zakresem jest przycinana.
- Na końcu sekwencji: przy `Loop == true` pozycja wraca na początek, przy
  `Loop == false` odtwarzanie się zatrzymuje na ostatniej klatce.
- `FrameChanged` wołane tylko przy zmianie indeksu klatki — UI nie musi
  sprawdzać, czy coś się zmieniło.

## 4. App — integracja

`MainViewModel` dostaje:

- `Sequence? Sequence` — wczytana sekwencja,
- `SequencePlayer? Player` — odtwarzacz,
- `bool HasSequence` — czy pasek sterowania ma być widoczny,
- `string FrameInfo` — np. „Klatka 12 / 300 (t=1,2 s)",
- komendy `PlayPauseCommand`, `LoadSequenceCommand`.

Przepływ:
1. Użytkownik podaje katalog (przycisk obok pola ścieżki) → `SequenceLoader.Load`.
2. `Pipeline.Reset()` — filtry z poprzedniego pliku nie przenoszą się na
   sekwencję.
3. `Player = new SequencePlayer(sequence)`; subskrypcja `FrameChanged` →
   `ShowFrame(index)`.
4. `ShowFrame` wczytuje klatkę i przepuszcza ją przez pipeline tak samo jak
   plik: `Pipeline.Source = frame; PointCloud = Pipeline.Output`.
5. Timer UI (`DispatcherTimer`, 30 ms) woła `Player.Advance(delta)`; zmiana
   klatki sama aktualizuje scenę.

**Zasada:** pipeline ma jedno źródło — plik albo bieżąca klatka. Kod ścieżki
pliku i klatki nie dubluje się: oba kończą w `ApplySource`-opodobnej metodzie,
która ustawia `Pipeline.Source` i `PointCloud`.

Filtry działają per klatka (przeliczane przy każdej zmianie klatki). Dla
wolnych filtrów (odosobione punkty) użytkownik wyłącza etap.

## 5. App — pasek sterowania

Pod paskiem narzędzi, widoczny tylko gdy `HasSequence`:

- przycisk **Odtwórz / Pauza**,
- suwak osi czasu (`Slider` 0..Duration, krok 0.01) — przewijanie,
- `ComboBox` prędkości: 0.25x, 0.5x, 1x, 2x, 4x,
- `CheckBox` „Pętla",
- `TextBlock` „Klatka 12 / 300".

Suwak nie może być źródłem prawdy podczas przeciągania — pozycję ustawia
odtwarzacz, a UI tylko ją pokazuje i wysyła `Seek` przy zmianie.

## 6. Błędy

- Katalog nie istnieje → „Katalog sekwencji nie istnieje: {ścieżka}."
- `sequence.json` nie jest poprawnym JSON-em → „Nie udało się odczytać sequence.json: {szczegół}."
- Brak pliku klatek → komunikat jak w §1.
- Każdy błąd trafia do `Status` — aplikacja nie przerywa działania.

## 7. Testy

**Core** (`LidarViewer.Core.Tests/Playback/`):
- wczytanie manifestu z katalogu z dwiema klatkami PLY → 2 klatki, nazwa,
  czasy 0.0 i 0.1;
- brak `sequence.json` → `InvalidDataException` z komunikatem;
- pusta lista klatek → `InvalidDataException`;
- brak pliku klatki → `InvalidDataException`;
- czasy nierosnące → `InvalidDataException`;
- klatka bez `timestamp` → czas `indeks * 0.1`;
- `LoadFrame` zwraca punkty z pliku klatki;
- odtwarzacz: `Advance` po `IsPlaying == false` nie zmienia kadru;
- `Advance` przesuwa klatkę zgodnie z czasami klatek;
- `Seek` trafia w klatkę zawierającą podany czas;
- `Speed` mnoży upływ czasu (2x → dwa kadry w tym samym delta);
- koniec sekwencji bez pętli → zatrzymanie na ostatniej klatce;
- koniec sekwencji z pętlą → powrót na początek.

**App** (`LidarViewer.App.Tests/`):
- wczytanie sekwencji czyta klatkę i ustawia `PointCloud`;
- `PlayPause` przełącza stan odtwarzacza;
- `Seek` zmienia klatkę i scenę;
- `FrameInfo` pokazuje numer klatki z liczby klatek.

## 8. Ryzyka

- **Duże pliki:** wczytanie klatki trwa tyle, ile trwa czytnik (2–50 ms dla
  przykładowych plików). Przy 10 kl/s to do 20% czasu procesu — wystarczająco,
  by nie zamarzać.
- **Filtry per klatka:** `StatisticalOutlierFilter` na 345 tys. punktów trwa
  ~0,4 s. Odtwarzanie będzie wtedy nierealne — dlatego liczniki klatek
  aktualizują się przy zmianie kadru, a nie co tick.
