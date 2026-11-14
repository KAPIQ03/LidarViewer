# Etap 4 - podglad na zywo po UDP - specyfikacja

**Cel:** LidarViewer odbiera chmury punktow z czujnika (lub symulatora) przez UDP i pokazuje je w oknie na zywo.

**Architektura:** Cala logika odbioru jest w `LidarViewer.Core/Streaming` i nie zalezy od Avalonii. Warstwa aplikacji tylko podpina odbiorc do modelu widoku i przekazuje chmure do kontrolki 2D/3D.

## Decyzje projektowe

### Dwa formaty pakietow

Nie wiadomo, jaki czujnik uzytkownik posiada, wiec format jest rozszerzalny, a w pierwszej wersji obslugiwane sa dwa:

1. **Wlasny protokul LVLR** - zaprojektowany w tym etapie. Prosty, otwarty, uzywany przez wbudowany symulator i testy.
2. **Velodyne VLP-16** - natywny format danych czujnika (pakiet 1206 B, port 2368). Implementacja na podstawie sterownika ROS `ros-drivers/velodyne`.

Kazdy format ma osobny dekoder implementujacy interfejs `IPacketDecoder`. Dodanie kolejnego czujnika to nowa klasa, bez zmian w rescie programu.

### Protokol LVLR

Naglowek 10 bajtow:

| Offset | Rozmiar | Tresc |
|---|---|---|
| 0..3 | 4 | magika `LVLR` |
| 4 | 1 | wersja protokolu (1) |
| 5 | 1 | flagi, zarezerwowane |
| 6..9 | 4 | liczba punktow, uint32 little endian |

Nastepnie dla kazdego punktu 13 bajtow: `x`, `y`, `z` (float32 little endian) oraz intensywnosc (1 bajt).

### Pakiet Velodyne VLP-16

Pakiet ma 1206 bajtow i sklada sie z 12 blokow po 100 bajtow oraz 6 bajtow naglowka pakietu.

Blok: 2 bajty naglowka banku (`0xEEFF` dla VLP-16), 2 bajty azymutu (dziesiate stopnia, 0..35999), 96 bajtow danych: 2 strzalow x 16 laserow x 3 bajty (dystans uint16 w jednostkach 2 mm + refleksyjnosc).

Katy pionowe laserow 0..15 (rad) oraz rozdzielczosc dystansu 0.002 m pochodza z pliku kalibracji `VLP16db.yaml` ze sterownika ROS. Korekty per-laser (dystans, azymut) sa pomijane - wymagalyby pliku kalibracji konkretnego egzemplarza czujnika.

Azimut kazdego punktu jest interpolowany wzgledem czasu strzalu, tak jak w sterowniku ROS: `azimuth + diff * (dsr*2.304 + firing*55.296) / 110.592`.

### Bufor punktow

Chmura na zywo jest **kumulowana** - widac ostatnie pakiety, nie tylko ostatni. Bufor ma ograniczona pojemnosc (domyslnie 200 000 punktow) i jest buforem cyklicznym: po zaplaceniu kasuje najstarsze punkty. Dzieki temu zywo nie wycieka pamiec, a widok pokazuje "ostatni obrot".

### Odswiezanie widoku

Odbiorca wysyla zdarzenie z nowa chmura maksymalnie 10 razy na sekunde. Dzieki temu interfejs nie dlawi sie przy szybkim strumieniu, a widok jest plynniejszy niz przy 10 Hz.

### Filtry w trybie live

Filtry z panelu **nie uruchamiaja sie** w trybie live. VoxelGrid na 200 tys. punktow zajmuje ~17 ms, ale StatisticalOutlier ~400 ms - przy 10 Hz nie nadaje sie do interaktywnego podgladu. W trybie live widoczna jest chmura surowa, a pasek filtrow jest wyszarzony. Filtry dzialaja normalnie dla wczytanych plikow.

### Kamera

Przy kazdej aktualizacji chmury kontrolka 3D przeladowuje bufor werbow. W trybie live nie przestawia to kamery - uzytkownik musi miec mozliwosc obrotu widoku. Kamera dopasowuje sie do chmury tylko przy pierwszej klatce trybu live (i przy kazdym wczytaniu pliku).

### Symulator

Wbudowany symulator wysyla syntetyczne "pakiety" do `127.0.0.1` co 50 ms. Generuje wirujacy wiatrak punktow o promieniu 8-12 m. Pozwala sprawdzic cale lancuchy bez sprzetu.

### Bladowanie

Odbiorca nie crashuje przy blednym pakiecie - pakiety nierozpoznane sa zliczane jako odrzucone i widoczne w statusie. Blad w watku odbioru konczy tylko ten watek, a nie aplikacje.

## Elementy

**Core (`src/LidarViewer.Core/Streaming`)**

| Plik | Odpowiedzialnosc |
|---|---|
| `IPacketDecoder.cs` | Kontrakt dekodera pakietow |
| `SimpleCloudProtocol.cs` | Format LVLR + kodowanie pakietow |
| `SimpleCloudDecoder.cs` | Dekoder LVLR |
| `VelodyneVlp16Decoder.cs` | Dekoder pakietow VLP-16 |
| `LiveCloudBuffer.cs` | Kumulujacy bufor punktow o stalej pojemnosci |
| `UdpCloudReceiver.cs` | Gniazdo UDP, watk odbioru, odpalanie zdarzen |

**App (`src/LidarViewer.App`)**

| Plik | Odpowiedzialnosc |
|---|---|
| `Streaming/LiveSimulator.cs` | Generator i nadawca pakietow symulacyjnych |
| `ViewModels/LiveProtocol.cs` | Wybor formatu pakietow w UI |
| `ViewModels/MainViewModel.cs` | Start/stop odbioru, przekazanie chmury do widoku |
| `Views/MainWindow.axaml` | Pasek trybu live |

## Przeplyw danych

```
UDP -> UdpCloudReceiver -> IPacketDecoder -> LiveCloudBuffer -> zdarzenie CloudUpdated
                                                                    |
                                   MainViewModel (watek odbioru) -> zdarzenie LiveCloudUpdated
                                                                    |
                                   MainWindow -> Dispatcher.UIThread -> vm.ApplyLiveCloud
                                                                    |
                                              PointCloud -> kontrolka 2D/3D
```

Przeniesienie na watek UI dzieje sie w warstwie widoku, nie w modelu widoku - dzieki temu testy modelu widoku nie wymagaja initialization Avalonii.

## Testy

- Dekoder LVLR: poprawny pakiet, zla magika, za krotki pakiet, niezgodna liczba punktow.
- Dekoder VLP-16: budowa pakietu testowego, poprawny dystans/kat, odrzucenie pakietu z blednym naglowkiem banku.
- Bufor: kumuluje punkty, przekracza pojemnosc i kasuje najstarsze, snapshot ma poprawna kolejnosc.
- Odbiorca: pakiet UDP na loopback daje zdarzenie z chmura, zly pakiet jest odrzucony.
- Model widoku: start/stop odbioru, chmura trafia do `PointCloud`, symulator generuje pakiety.

## Poza zakresem

- VLP-32C i inne czujniki Velodyne (inny uklad bloku) - architektura jest gotowa, brakuje dekodera.
- Zapis strumienia do pliku (to etap 6).
- Kalibracja per-laser dla konkretnego czujnika.
