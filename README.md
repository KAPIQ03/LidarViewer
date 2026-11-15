# LidarViewer

Przegladarka chmur punktow z czujnikow lidar. Aplikacja desktopowa (.NET 10, Avalonia 12,
OpenGL) do odtwarzania plikow i nagran z chmurami punktow, ogladania ich w 2D i 3D,
filtrowania, podlaczenia do czujnika Velodyne na zywo przez UDP oraz nagrywania i eksportu.

Wzorem interfejsu i zachowania jest VeloView 5.1.

## Stan projektu

Zaimplementowane na branchu `feat/lidar-toolkit`:

| Zakres | Status |
|---|---|
| Wczytywanie plikow (VTP, VTK, CSV, PLY, PCD, LAS), widok 3D i 2D | gotowe |
| Kolorowanie, mapy kolorow, filtry i pipeline filtrow | gotowe |
| Interfejs w stylu VeloView (menu, pasek ikon, panel boczny, pasek stanu, kostka orientacji) | gotowe |
| Odtwarzanie nagran pcap | gotowe |
| Nagrywanie (surowe pakiety do pcap, klatki PLY) | gotowe |
| Tryb na zywo: Velodyne (VLP-16, HDL-32E, db.xml) i protokol wlasny | gotowe |
| Eksport (PLY, PCD, LAS, CSV/XYZ), zrzut widoku do PNG | gotowe |
| Narzedzia: pomiar odleglosci, tabela punktow, siatka i osie | gotowe |

## Uruchomienie

```bash
dotnet run --project src/LidarViewer.App
```

Testy:

```bash
dotnet test LidarViewer.slnx
```

Na macOS aplikacja wymusza renderowanie przez OpenGL (`AvaloniaNativeRenderingMode.OpenGl`),
bo domyslny Metal nie wspolpracuje z kontrolka OpenGL. Bledy GL widac w logu diagnostycznym
(menu `Pomoc`).

## Interfejs

- **Menu**: Plik, Widok, Live, Narzedzia, Pomoc.
- **Pasek ikon**: po lewej otwieranie plikow, zapis, zrzut ekranu, start i stop trybu
  na zywo, siatka, osie, pomiar, dopasowanie widoku. Po prawej przyciski `Tabela punktow`
  i `Panel boczny`.
- **Panel boczny** (Expandery): Wyglad, Zrodlo na zywo, Filtry.
- **Pasek stanu**: liczba punktow, zrodlo danych, a w trybie live pakiety/s, klatki/s i liczba
  odrzuconych pakietow.
- **Kostka orientacji** w rogu widoku 3D: klik w sciane ustawia kamere wzdluz osi.
- Motyw ciemny, ikony z `Material.Icons.Avalonia`.

## Funkcje

### Wczytywanie plikow

Obslugiwane formaty: `.vtp` (appended/raw, bez kompresji), `.vtk` (legacy, ASCII i BINARY),
`.csv`, `.xyz`, `.ply` (ascii/binary), `.pcd` (ascii/binary/binary_compressed), `.las` 1.0-1.4
(formaty 0-10, bez LAZ). Wariant nieobslugiwany konczy sie komunikatem o bledzie.

Czytniki znajduja sie w `LidarViewer.Core/Readers` (VTP w `Core/Vtp`), a `PointCloudLoader`
wybiera czytnik po rozszerzeniu i usuwa punkty NaN/Inf. Kazdy czytnik zwraca ten sam model
`PointCloud`.

### Atrybuty punktow

Oprocz pozycji `PointCloud` moze niesc:

- `intensity`, `category` - z pliku
- `LaserId`, `Azimuth`, `Time` - z dekodera Velodyne
- `distance` - odleglosc od czujnika, `height` - wysokosc swiatowa (liczone na zadanie)

`Origin` (double) to przesuniecie ukladu (LAS). Pozycje w buforze GPU sa liczone wzgledem
srodka chmury.

### Kolorowanie

Tryby koloru: `Solid` (domyslnie szary), `Intensity`, `Distance`, `Height`, `LaserId`,
`Azimuth`, `Time`.

Mapy kolorow: `Grayscale`, `Viridis`, `Turbo`, `Jet`.

Zakres mapowania moze byc ustalony automatycznie albo recznie; legenda z gradientem jest
widoczna w rogu widoku. Rozmiar punktu reguluje suwak.

### Filtry

Filtry sa uporzadkowane w potoku. Kazdy etap pokazuje liczbe punktow na wejsciu
i wyjsciu, a kolejnosc etapow mozna zmieniac. Filtry dzialaja takze w trybie na zywo.

- Zakres skalara (np. po `LaserId`)
- Przyciecie prostopadloscianem i kula
- Siatka voxelowa
- Usuwanie odosobionych (filtr statystyczny)
- Usuwanie ziemi (RANSAC)

### Widok 2D i 3D

Widok 3D rysuje punkty w OpenGL (Silk.NET) z kamera orbitalna, siatka kolowa i osiami
(rysowanymi na wierzchu siatki). Widok 2D rzutuje chmure na plaszczyzne XY, XZ albo YZ.
Przelaczanie klawiszami `2` i `3`.

### Pomiar i tabela punktow

- **Pomiar odleglosci**: dwa klikniete punkty (najblizszy punkt w pikselach), odcinek i krzyzyki
  w widoku 3D.
- **Tabela punktow**: zwirtualizowana lista punktow z atrybutami (przycisk `Tabela punktow`).

### Nagrania pcap

Odtwarzac mozna **nagranie `.pcap` / `.pcapng`** z Velodyne (pakiety UDP 1206 B): klatki wyznaczane sa wg
  obrotow czujnika (przejscie azymutu przez 360 stopni), indeks budowany jest raz, a klatki
  dekodowane leniwie i trzymane w cache (`PcapFrameSource`); bez plikow posrednich.

Odtwarzacz ma: Play/Pauza, suwak, krok klatka po klatce (←/→), skok do pierwszej i ostatniej
klatki (Home/End), zakres klatek, tempo od 0.1x, petle oraz "slad" (doklejanie do 10
poprzednich klatek).

### Tryb na zywo (UDP)

Odbiornik UDP nasluchuje na wybranym porcie. Protokoly:

- **Velodyne (kalibracja)** - natywne pakiety czujnika, port domyslnie 2368. Modele
  wbudowane: VLP-16 i HDL-32E; inne (VLP-32, HDL-64E) po wczytaniu pliku kalibracji `db.xml`
  (przycisk `Kalibracja...`). Strumien dzielony jest na obroty i pokazywany jest ostatni pelny
  obrot, jak w VeloView.
- **Protokol wlasny LVLR** - naglowek 10 B (`LVLR`, wersja, flagi, liczba punktow), potem 13 B
  na punkt (`x`, `y`, `z` float32 LE plus 1 B intensywnosci); bufor kumuluje punkty.

Checkbox `Symulator` w panelu bocznym uruchamia wbudowany symulator. Jesli w drzewie katalogow
nad aplikacja znajdzie sie przykladowe nagranie `lidar-samples/VLP-16 Sample Data/2015-07-23-14-37-22_Velodyne-VLP-16-Data_Downtown 10Hz Single.pcap`,
symulator odtwarza je w petli, w oryginalnym tempie, jako pakiety Velodyne VLP-16 (aplikacja sama
ustawia protokol i czujnik). Bez tego pliku wysyla scene syntetyczna w protokole LVLR. Awarie gniazda UDP sa zglaszane
w interfejsie, a ostatnia porcja danych jest publikowana nawet przy zakonczeniu strumienia.

#### Nadajnik testowy Velodyne

`tools/velodyne_sender.py` (sama biblioteka standardowa Pythona) symuluje czujnik VLP-16:

```bash
python3 tools/velodyne_sender.py                       # scena syntetyczna
python3 tools/velodyne_sender.py --pcap nagranie.pcap --loop   # odtwarzanie pcap
python3 tools/velodyne_sender.py --loss 0.05           # gubi 5% pakietow
```

W aplikacji: panel boczny → `Zrodlo na zywo` → protokol `Velodyne (kalibracja)`, VLP-16,
port 2368 → `Start`.

### Nagrywanie

Przyciski `Nagrywaj` i `Stop nagrania` pojawiaja sie w trybie na zywo z protokolem Velodyne.

Surowe pakiety zapisywane sa do jednego pliku `.pcap` (Ethernet/IPv4/UDP z suma kontrolna IP),
ktory otwiera sie w aplikacji, Wireshark i VeloView.

Zapis idzie w watku roboczym, a zamkniecie okna konczy trwajace nagranie.

### Eksport i zrzuty

`Zapisz jako...` zapisuje chmure widoczna w scenie (po filtrach) jako `PLY`, `PCD` (binary),
`LAS` (1.2, format 0) albo `CSV`/`XYZ`. Zrzut widoku do `PNG` robi przycisk aparatu
(w 3D odczyt bufora ramki, w 2D `RenderTargetBitmap`).

### Skroty klawiszowe

| Klawisz | Dzialanie |
|---|---|
| `R` | reset widoku |
| `2` / `3` | widok 2D / 3D |
| `Spacja` | odtwarzanie / pauza |
| `←` / `→` | klatka wstecz / do przodu |
| `Home` / `End` | pierwsza / ostatnia klatka |

Skroty nie dzialaja, gdy fokus jest w polu tekstowym, combo boxie, checkboxie lub slidere.

## Struktura projektu

```
src/LidarViewer.Core/          logika, bez zaleznosci od UI
  Models/                      PointCloud, PointTable, atrybuty, plaszczyzny rzutu
  Readers/                     czytniki plikow (CSV, PLY, PCD, LAS, VTK)
  Vtp/                         czytnik VTP
  Export/                      zapis chmur (PLY, PCD, LAS, CSV/XYZ)
  Filters/                     filtry i pipeline
  Rendering/                   kolory, mapy kolorow, kamera orbitalna, siatka i osie,
                               kostka orientacji, wybieranie punktu
  Spatial/                     kd-tree, dopasowanie plaszczyzny
  Streaming/                   odbiornik UDP, dekodery Velodyne, kalibracja, czytnik
                               i zapis pcap, bufor live, miernik pakietow
  Playback/                    zrodlo klatek (pcap), odtwarzacz, cache

src/LidarViewer.App/           warstwa aplikacji (Avalonia)
  Views/MainWindow.axaml       okno glowne
  ViewModels/                  MainViewModel (partial: Playback, Live, Recording),
                               pipeline, edytory filtrow
  Controls/                    widok 3D (OpenGL), widok 2D, legenda, kostka orientacji
  Streaming/                   symulator strumienia
  Converters/                  konwertery wartosci dla XAML

tools/velodyne_sender.py       nadajnik pakietow Velodyne VLP-16 do testow live

tests/LidarViewer.Core.Tests/             testy logiki
tests/LidarViewer.App.Tests/              testy modelu widoku
tests/LidarViewer.App.Headless.Tests/     testy okna (Avalonia.Headless)
```

`LidarViewer.Core` nie wie nic o Avalonii - cala logika jest testowalna bez uruchamiania
okna.

## Zasady kodu

- Logika w `Core` bez zaleznosci od UI, warstwa aplikacji tylko podpina ja do widoku
- Testy pisane najpierw (TDD)
- Male, wysoko spojne pliki
- Komentarze i komunikaty dla uzytkownika po polsku, nazwy w kodzie po angielsku
