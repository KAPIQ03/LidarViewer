# LidarViewer

Desktopowa aplikacja Avalonia do podglądu i podstawowej analizy chmur punktów LIDAR.

## Uruchomienie

Projekt znajduje się w folderze `LidarViewer`. Jeśli terminal jest otwarty w katalogu rozwiązania (`C:\Users\Dell\Desktop\LIDAR\LidarViewer`), uruchom:

```powershell
dotnet run --project src\LidarViewer.App\LidarViewer.App.csproj
```

Jeśli terminal jest otwarty folder wyżej (`C:\Users\Dell\Desktop\LIDAR`), użyj ścieżki z dodatkowym katalogiem `LidarViewer`:

```powershell
dotnet build .\LidarViewer\src\LidarViewer.App\LidarViewer.App.csproj
dotnet run --project .\LidarViewer\src\LidarViewer.App\LidarViewer.App.csproj
```

W zadaniu VS Code ścieżka projektu również musi zawierać `LidarViewer`, np. `C:\Users\Dell\Desktop\LIDAR\LidarViewer\src\LidarViewer.App\LidarViewer.App.csproj`.

Testy:

```powershell
dotnet test LidarViewer.slnx
```

Z katalogu nadrzędnego testy można uruchomić poleceniem `dotnet test .\LidarViewer\LidarViewer.slnx`.

## Dane wejściowe

- **VTP** — pliki `PolyData` z punktami XYZ zapisanymi jako tekstowe (`format="ascii"`) tablice `DataArray`. Opcjonalne wartości intensywności są odczytywane z tablicy `PointData` o nazwie zawierającej `intensity` lub `reflect`; w przeciwnym razie używana jest pierwsza tablica jednowymiarowa. Binarne i appended `DataArray` nie są obecnie obsługiwane.
- **CSV** — aplikacja wczytuje kolumny `x,y,z,intensity` lub własny format nagrania `timestamp_utc,x,y,z,intensity`. Separatorem jest przecinek, a liczby mają format invariant (kropka dziesiętna).

## Odbiór na żywo

Wybierz port UDP i uruchom odbiór. Każdy datagram powinien zawierać UTF-8 JSON w jednym z formatów:

```json
{"points":[[1.0,2.0,3.0,0.8],[4.0,5.0,6.0,0.4]]}
```

lub:

```json
{"points":[{"x":1.0,"y":2.0,"z":3.0,"intensity":0.8}]}
```

Intensywność jest opcjonalna. Odbiornik nasłuchuje na wszystkich interfejsach sieciowych hosta. Aplikacja nie dekoduje natywnych pakietów konkretnego producenta — urządzenie lub zewnętrzna bramka musi wysyłać punkty w powyższym formacie.

Nagrywanie jest dostępne podczas aktywnego odbioru UDP. Zapisany plik CSV można później ponownie otworzyć w aplikacji.

## Nawigacja 3D

- przeciąganie lewym przyciskiem myszy — obrót kamery,
- przeciąganie prawym przyciskiem lub `Shift` + przeciąganie — przesunięcie widoku,
- kółko myszy — przybliżenie/oddalenie,
- przycisk **Reset kamery** — przywrócenie widoku początkowego.

Panel podglądu pokazuje liczbę punktów, zakres XYZ i średnią intensywność.
