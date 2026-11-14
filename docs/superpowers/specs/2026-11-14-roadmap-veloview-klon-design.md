# Roadmap: mniejszy klon VeloView/LidarView 5.2

Zatwierdzany etapami — 2026-11-14. Każdy etap to osobny commit na branchu `feat/veloview-clone`.

## Cel

Aplikacja desktopowa (.NET 10 / Avalonia / OpenGL), która:

1. otwiera pliki chmur punktów (CSV, PLY, PCD, LAS, VTK, VTP — już działa),
2. koloruje punkty wg intensywności lub odległości od sensora,
3. stosuje filtry CPU w łańcuchu (pipeline),
4. odtwarza pliki `.pcap` z poruszającą się chmurą (ruch sensoru),
5. odbiera strumień UDP na żywo z lidara Velodyne,
6. nagrywa strumień do `.pcap` i eksportuje dane/widoki.

Referencja wyglądu i funkcji: LidarView 5.2 (następca VeloView, bazowany na VTK/ParaView).
Nie jest to klon 1:1 — brak klienta/serwera, Pythona, SLAM, EDT i multi-sensor.

## Decyzje (ustalone z użytkownikiem)

| Obszar | Decyzja |
|---|---|
| Sensor UDP | Velodyne VLP-16 / VLP-32 / HDL-64 (wspólny format „data packet") |
| Format nagrań | `.pcap` (LINKTYPE_ETHERNET, zgodny z LidarView i Wireshark) |
| Interfejs | Styl LidarView/ParaView: pipeline po prawej + zakładki Display/Properties |
| Kolorowanie | Intensywność oraz dystans od sensora (oba z mapą kolorów i legendą) |

## Stan obecny

- `LidarViewer.Core` — model `PointCloud` (pozycje + intensity + category), czytniki 6 formatów,
  `OrbitCamera`. Bez zależności od Avalonia, w pełni testowalny.
- `LidarViewer.App` — okno z polem ścieżki, przełącznik 2D/3D, `PointCloudView3D` (Silk.NET OpenGL,
  jeden kolor dla całej chmury), `PointCloudView` (rasteryzacja 2D na `WriteableBitmap`).
- Testy xUnit dla czytników i kamery.

Brakuje: kolorów per-punkt, atrybutów skalarnych, filtrów, pipeline, dekodera UDP, pcap,
odtwarzania, UI w stylu LidarView, nagrywania.

## Architektura docelowa

```
                 ┌──────────────── Core (bez UI, testowalne) ────────────────┐
  plik .ply ───►│ Readers ──► PointCloud ──► Filters ──► Pipeline ─┐        │
  plik .pcap ──►│ PcapReader ──► VelodyneDecoder ──► FrameSource ──┼──► View│
  UDP :2368 ──►│ UdpReceiver ───┘                                   │        │
                 └───────────────────────────────────────────────────┴────────┘
                                        │
                          App: VM ← pipeline model → kontrolki 3D (OpenGL)
```

### Kluczowe typy (Core)

```csharp
// Punkt + atrybuty skalarne (ParaView-style "point arrays")
public sealed class PointCloud {
    float[] Positions;          // 3 na punkt
    byte[]? Intensity;          // 1 na punkt
    byte[]? Rgb;                // 3 na punkt
    IReadOnlyList<PointAttribute> Attributes;  // nazwane skale float
}

public sealed record PointAttribute(string Name, float[] Values, float Min, float Max);

// Klatka = chmura + czas
public sealed record CloudFrame(PointCloud Cloud, DateTime Timestamp);

// Abstrakcja źródła klatek — wspólna dla odtwarzania pliku i streamu live
public interface IFrameSource {
    IAsyncEnumerable<CloudFrame> Frames { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}
```

### Warstwa GPU

Bufor pozycji i bufor kolorów są **rozdzielone**, żeby zmiana trybu kolorowania nie
przesyłała ponownie pozycji:

| Bufor | Format | Kiedy się aktualizuje |
|---|---|---|
| `_posVbo` | vec3 × f32 (pozycje względem środka chmury) | przy zmianie chmury |
| `_colVbo` | vec4 × u8 znormalizowane | przy zmianie trybu/range kolorów |

Kolory liczone na CPU (przeliczenie ~10 ms dla 1 mln pkt, wykonywane tylko przy zmianie
ustawień). Shadery odpowiadają za wielkość punktu, głębię i oświetlenie.

## Kolejność etapów i kryteria akceptacji

Zależności: kolory → filtry → UI → odtwarzanie → live → nagrywanie/eksport.
UI powstaje w dwóch porcjach: minimalny w etapie 1 (toolbar), pełny w etapie 3.

### Etap 1 — Kolory i atrybuty punktów

- `PointCloud` dostaje `Rgb` i listę nazwanych atrybutów skalarnych.
- Atrybuty pochodne: `Distance` (od punktu `Origin`), `Height` (Z).
- Tryby kolorowania: **Solid**, **Intensity**, **Distance**. Zakres min/max ręczny
  i „dopasuj do danych". Mapy kolorów: viridis, turbo, grayscale, jet.
- Legenda kolorów w UI (pasek z gradientem + podpisy min/max).
- Przycisk „przerysuj" nie jest potrzebny — kolor przeliczany przy zmianie ustawień.

*Akceptacja:* otwarta chmura PLY/PCD pokazuje się w skali szarości lub turbo; przełącznik
zmienia tryb w mgnieniu oka; legenda pokazuje zakres; brak spadków klatek przy 1 mln pkt.

### Etap 2 — Filtry i pipeline

Filtry CPU w Core, każdy jako czysta funkcja `PointCloud -> PointCloud`:

| Filtr | Co robi |
|---|---|
| `ScalarRangeFilter` | wycina po zakresie wybranego skalaru (intensity/distance/height) — „Select Points by Scalars" z LidarView 5.2 |
| `BoxCropFilter` | przycina po prostokącie X/Y/Z (VeloView „Crop returns") |
| `SphereCropFilter` | przycina po promieniu od sensora |
| `VoxelGridFilter` | redukcja do siatki sześciennej (odpowiednik PCL VoxelGrid) |
| `StatisticalOutlierFilter` | usuwa punkty odosobowane (odległość do k-neighbours) |
| `GroundRemovalFilter` | RANSAC — dopasowuje płaszczyznę i usuwa ziemia |

Pipeline to lista etapów z włącznikami; wynik ostatniego etapu trafia do widoku.
Każdy filtr ma okno parametrów z **Zastosuj / Anuluj** (styl ParaView).

*Akceptacja:* da się złożyć łańcuch ground-removal → voxel-grid → intensity-range;
każdy etap da się wyłączyć i zmienić parametry bez reloadu pliku; licznik punktów
w pasku statusu aktualizuje się po każdym etapie.

### Etap 3 — Interfejs w stylu LidarView

- Prawy panel: **Pipeline Browser** (drzewo źródeł i filtrów), zakładka **Display**
  (tryb koloru, zakres, rozmiar punktu, włącz/wyłącz), zakładka **Filters** (lista + dodaj).
- Toolbar: Otwórz, Live, Nagraj, Play/Pause, 2D/3D, Reset kamery, siatka, osie.
- Pasek statusu: liczba punktów, fps, źródło (plik / UDP host:port), wskaźnik LIVE.
- Widok 2D jako zakładka obok 3D (plan view / mapa intensywności).
- Przyciski kamery: −X +X −Y +Y −Z +Z, reset, zoom do danych (z VeloView).

*Akceptacja:* układ i zachowanie przypominają LidarView 5.2; wszystkie kontrolki
w panelu są żywe; brak martwych przycisków.

### Etap 4 — Ruchome pliki: odtwarzanie `.pcap`

- `PcapReader` — nagłówek globalny + rekordy; obsługuje LINKTYPE_ETHERNET (domyślny),
  LINKTYPE_RAW i LINKTYPE_NULL.
- `VelodyneDecoder` — dane z pakietów UDP na punkty: blok 100 B = flaga 0xFFEE,
  azymut (0,01°), 32 × (dystans 12 bitów w 2 mm + indeks reflektancji 4 bity);
  interpolacja azymutu między blokami; kalibracja pionowa per laser (VLP-16 wbudowana).
- Detekcja klatek: nowa klatka, gdy azymut wraca do zera.
- Odtwarzanie: play/pause, suwak czasu, prędkość (0.25× / 1× / 4×), loop,
  pomijanie klatek (frame step).

*Akceptacja:* nagrany `.pcap` z VLP-16 odtwarza się jako animacja z płynnym
obrotem punktów; suwak przewija klatki; odtwarzanie jest synchroniczne z licznikiem fps.

### Etap 5 — Live po UDP (Velodyne)

- `UdpReceiver` — `Socket` (UDP, multicast nieobowiązkowy), bufor 65536 B,
  odbiór w tle, dostarczanie klatek przez `IAsyncEnumerable`.
- Okno konfiguracji sensora: model (VLP-16/32/64), port (domyślnie 2368),
  plik kalibracji (YAML — opcjonalnie, format Velodyne), ignorowanie pustych klatek.
- Wskaźnik LIVE w pasku statusu + licznik fps i zgubionych pakietów.
- Ten sam `VelodyneDecoder` co w etapie 4 — brak duplikacji logiki.

*Akceptacja:* podłączony VLP-16 po Wi-Fi/ethernet wysyła punkty w czasie rzeczywistym;
aplikacja nie zamarza przy 10 Hz; można zatrzymać i wznowić odbiór.

### Etap 6 — Nagrywanie i eksport

- `PcapWriter` — zapis surowych pakietów UDP do `.pcap` (LINKTYPE_ETHERNET z
  syntetycznym nagłówkiem Ethernet/IPv4/UDP, dokładnie jak robi LidarView).
- Przycisk **Nagraj** — zapisuje strumień live do pliku; zatrzymanie zamyka plik poprawnie.
- Eksport danych: bieżąca chmura do `.ply` / `.pcd` / `.csv` / `.las`.
- Eksport widoku: zrzut ekranu `.png` z aktualnego viewportu.
- Sesja odtwarzania zapisana jako `.pcap` → można otworzyć w Wireshark i LidarView.

*Akceptacja:* nagrany plik otwiera się we Wireshark z rozpoznanymi pakietami UDP;
ten sam plik otwiera się w LidarView 5.2 i pokazuje ruch sensoru.

## Ryzyka i ograniczenia

- **UDP live wymaga sprzętu.** Bez podłączonego lidara etap 5 testujemy na nagranym
  `.pcap` odtwarzanym w pętli (tryb „replay as live").
- **Atrybuty intensywności.** PLY/PCD/LAS mają intensity, CSV i VTP nie zawsze.
  Brak intensity → tryb kolorowania Intensity jest niedostępny (UI pokazuje to wyraźnie).
- **Korekcja dystorsji.** Pełna korekcja zniekształceń wymaga kalibracji per-laser
  i timestampów; w etapie 4 robimy uproszczoną (azymut per pakiet, interpolacja).
  Wystarczy do podglądu, nie do pomiarów metrologicznych.
- **Wydajność.** Rasteryzacja 2D jest CPU-side (`WriteableBitmap`) — przy >2 mln pkt
  może działać wolno. Etap 3 ograniczy rysowanie 2D do próbek.

## Podział na commity

```
feat/veloview-clone
 ├─ docs: plan 6 etapów                          (ten dokument)
 ├─ feat(core): atrybuty punktów i odległość     etap 1
 ├─ feat(app): kolorowanie intensity/distance     etap 1
 ├─ feat(app): legenda i zakres kolorów           etap 1
 ├─ feat(core): filtry CPU + pipeline            etap 2
 ├─ feat(app): okno filtra i łańcuch             etap 2
 ├─ feat(app): panel pipeline/display/filters     etap 3
 ├─ feat(app): toolbar, status, view 2D          etap 3
 ├─ feat(core): czytnik pcap                     etap 4
 ├─ feat(core): dekoder Velodyne                  etap 4
 ├─ feat(app): odtwarzanie z osią czasu           etap 4
 ├─ feat(core): odbiór UDP + konfiguracja        etap 5
 ├─ feat(app): tryb live + okno sensora          etap 5
 ├─ feat(core): zapis pcap                       etap 6
 ├─ feat(app): nagrywanie i eksport              etap 6
 └─ feat(app): zrzut widoku PNG                  etap 6
```

## Testy

- Core: xUnit, 80%+ pokrycia. Testy dla atrybutów, map kolorów, każdego filtra,
  dekodera Velodyne (syntetyczny pakiet), round-trip pcap.
- App: budowanie i ręczna weryfikacja w UI. Testy E2E nieobowiązujące (Avalonia
  + OpenGL nie współpracuje z headless Playwright).

## Poza zakresem

SLAM/ICP, rejestracja wielosensorowa, ROS2/mcap, PDAL/LAZ, chmury rozproszone,
edytowanie meshów, pomiar odległości w 3D, eksport do wideo, serwer klienta.
