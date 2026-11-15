# LidarViewer

Przegladarka chmur punktow z czujnikow lidar. Aplikacja desktopowa (.NET 10, Avalonia,
OpenGL) do odtwarzania plikow z chmurami punktow, ogladania ich w 2D i 3D, filtrowania,
podlaczenia do czujnika na zywo przez UDP oraz nagrywania i eksportu.

Celem projektu jest mniejsza wersja VeloView / LidarView 5.2.

## Stan projektu

Wszystkie zaplanowane etapy sa zaimplementowane na branchu `feat/lidar-toolkit`:

| Etap | Zakres | Status |
|---|---|---|
| 1 | Kolory i atrybuty punktow | gotowe |
| 2 | Filtry i pipeline filtrow | gotowe |
| 3 | Sekwencje chmur punktow (odtwarzanie) | gotowe |
| 4 | Tryb live po UDP | gotowe |
| 5 | Uporzadkowany interfejs | gotowe |
| 6 | Nagrywanie i eksport chmur | gotowe |

## Uruchomienie

```bash
dotnet run --project src/LidarViewer.App
```

Testy:

```bash
dotnet test LidarViewer.slnx
```

## Funkcje

### Wczytywanie plikow

Obslugiwane formaty: `.vtp`, `.vtk`, `.csv`, `.xyz`, `.ply`, `.pcd`, `.las`.

Czytniki znajduja sie w `LidarViewer.Core/Readers`. Kazdy z nich zwraca ten sam model
`PointCloud`, wiec wszystkie formaty traktuja sie identycznie w dalszej obrabce.

### Atrybuty punktow

Kazdy punkt moze niech z pliku zostac zapisany w chmurze:

- `intensity` - intensywnosc odbicia odczytana z pliku (`.ply`, `.las`); plikow bez
  intensywnosci nie da sie kolorowac w trybie `Intensity`
- `distance` - odleglosc punktu od poczatku ukladu, liczona w podwojnej precyzji
- `height` - wysokosc jako wspolrzedna swiatowa (`z` + `Origin.Z`)

Atrybuty `distance` i `height` liczone sa na zadanie i cache`owane, wiec sa zawsze
dostepne niezaleznie od formatu pliku.

### Kolorowanie

Tryby koloru: `Solid`, `Intensity`, `Distance`.

Mapy kolorow: `Grayscale`, `Viridis`, `Turbo`, `Jet`.

Zakres mapowania moze byc ustalony automatycznie z chmury albo recznie (pole minimum
i maksimum). Reczny zakres jest zapamietywany i nie jest nadpisywany przez tryb auto.
Legenda z gradientem i podpisanym zakresem jest widoczna w rogu widoku.

### Filtry

Filtry sa uporzadkowane w potoku. Kazdy etap pokazuje liczbe punktow na wejsciu
i wyjsciu, a kolejnosc etapow mozna zmieniac przyciskami `▲` / `▼`.

Dostepne filtry:

- Zakres skalara - wycina punkty poza zakresem wybranego atrybutu
- Przyciecie prostopadloscianem
- Przyciecie kula
- Siatka voxelowa - redukuje chmure do jednego punktu na voxel
- Usuwanie odosobionych - filtr statystyczny (sasiedzi + prog odchylenia)
- Usuwanie ziemi - metoda RANSAC z iteracyjnym dopasowaniem plaszczyzny

Filtry dzialaja tylko na wczytanym pliku - w trybie live sa wylaczone.

### Widok 2D i 3D

Widok 2D rasteryzuje chmure do bitmapy w plaszczyznie XY, XZ albo YZ (kolor per punkt).

Widok 3D rysuje punkty w OpenGL (Silk.NET) z kamera orbitalna. Przełączanie widoku
przyciskami `2` i `3`.

### Sekwencje chmur punktow

Sekwencja to katalog z plikami klatek i manifestem `sequence.json`:

```json
{
  "Name": "nagranie",
  "Frames": [
    { "File": "frame_00000.ply", "Timestamp": 0.0 },
    { "File": "frame_00001.ply", "Timestamp": 1.0 }
  ]
}
```

Nagranie sieciowe z Velodyne VLP-16 (`.pcap` lub `.pcapng`, pakiety danych UDP 1206 B) mozna
otworzyc wprost przyciskiem wyboru pliku: aplikacja dzieli je na klatki (jeden obrot czujnika =
jedna klatka), zapisuje jako sekwencje w katalogu tymczasowym i odtwarza jak kazda inna.

Pasek odtwarzania ma tez krok klatka po klatce (←/→), skok do pierwszej i ostatniej klatki (Home/End),
wybor zakresu klatek, tempo od 0.1x oraz "slad" - doklejanie do 10 poprzednich klatek do widoku.

Manifest opisuje klatki na osi czasu, wiec sekwencja odtwarza sie jak film: przyciski
odtwarzania, przewijanie, wybor tempa i pętla.

### Tryb live po UDP

Odbiorca UDP nasluchuje na wybranym porcie i przekazuje chmury do widoku. Obslugiwane
sa dwa formaty pakietow:

- **Wlasny protokul LVLR** - naglowek 10 B (magika `LVLR`, wersja, flagi, liczba punktow),
  nastepnie 13 B na punkt (`x`, `y`, `z` jako float32 LE plus 1 B intensywnosci)
- **Velodyne VLP-16** - natywny format danych czujnika (pakiet 1206 B, port 2368)

Wbudowany symulator wysyla chmury w protokole LVLR, dzieki czemu tryb live da sie
przetestowac bez sprzetu. Kamera w trybie live jest stabilna - nie skacze przy kazdej
nowej chmurze.

### Nagrywanie

Przyciski `Nagrywaj` i `Stop nagrania` pojawiaja sie w trybie live. Nagrywanie tworzy
w wybranym katalogu podkatalog `nagranie-<data>-<godzina>/`, w ktorym zapisywane sa
klatki `frame_00000.ply`, `frame_00001.ply`, ... oraz manifest `sequence.json`.

Klatki zapisywane sa w watku roboczym, wiec interfejs nie zacina. Domyslnie zapisywana
jest jedna klatka na sekunde. Po zatrzymaniu nagrania manifest jest finalizowany i
nagranie od razu otwiera sie przez `Sekwencja...`.

Zamkniecie okna konczy trwajace nagranie, dzieki czemu manifest zawsze powstaje.

### Eksport

Przycisk `Zapisz jako...` zapisuje chmure widoczna w scenie (po filtrach) we
wspolrzednych swiatowych:

- `PLY` - binarny, razem z intensywnoscia, gdy chmura ja ma
- `XYZ` / `CSV` - tekstowy, `x,y,z` albo `x,y,z,intensity`

### Skroty klawiszowe

| Klawisz | Dzialanie |
|---|---|
| `R` | reset widoku |
| `2` | widok 2D |
| `3` | widok 3D |
| `Spacja` | odtwarzanie / pauza |

Skroty nie dzialaja, gdy fokus jest w polu tekstowym, combo boxie, checkboxie,
slidere lub przycisku.

## Struktura projektu

```
src/LidarViewer.Core/          logika, bez zaleznosci od UI
  Models/                      PointCloud, atrybuty punktow, plaszczyzny
  Readers/                     czytniki plikow
  Export/                      zapisywanie chmur (PLY, CSV/XYZ)
  Recording/                   nagrywanie sekwencji
  Filters/                     filtry i pipeline
  Rendering/                   kolory, mapy kolorow, kamera orbitalna
  Spatial/                     kd-tree, dopasowanie plaszczyzny
  Streaming/                   odbiorca UDP i dekodery pakietow
  Vtp/                         czytnik VTP
  Playback/                    model sekwencji i odtwarzacz

src/LidarViewer.App/           warstwa aplikacji
  Views/MainWindow.axaml       okno glowne
  ViewModels/                  model widoku, pipeline, edytory filtrow
  Controls/                    widok 2D, widok 3D, legenda kolorow
  Streaming/                   symulator strumienia
  Converters/                  konwertery wartosci dla XAML

tests/LidarViewer.Core.Tests/       testy logiki
tests/LidarViewer.App.Tests/        testy modelu widoku
tests/LidarViewer.App.Headless.Tests/   testy rozlozenia okna (Avalonia.Headless)
```

`LidarViewer.Core` nie wie nic o Avalonii - cala logika jest testowalna bez uruchamiania
okna.

## Zasady kodu

- Logika w `Core` bez zaleznosci od UI, warstwa aplikacji tylko podpina ja do widoku
- Testy pisane najpierw (TDD), minimum 80% pokrycia
- Male, wysoko spojne pliki
- Polskie komentarze w kodzie bez znakow diakrytycznych (tecz w komunikatach git)
