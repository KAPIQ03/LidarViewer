# Faza 1 – wczytanie pliku VTP i wyświetlenie chmury punktów

Zatwierdzone w rozmowie 2026-11-03.

## Cel
Podstawowa aplikacja (bez stylów): pole na ścieżkę pliku `.vtp` + przycisk „Wyświetl”,
który wczytuje chmurę punktów i rysuje ją w głównym widoku 3D (OpenGL).

## Decyzje
- Renderowanie: `OpenGlControlBase` (Avalonia) + Silk.NET.OpenGL, shadery GLSL 150.
- Kolor: jednolity (intensity/category tylko wczytywane do modelu).
- Parser: tylko VTK PolyData, `format="appended"`, `encoding="raw"`, bez kompresji,
  LittleEndian. Inne warianty -> `NotSupportedException`.

## Komponenty
- Core: `PointCloud`, `VtpReader`, testy xUnit (plik syntetyczny + opcjonalny test na prawdziwym pliku).
- App: `MainViewModel` (FilePath, LoadCommand, PointCloud, Status), `MainWindow`, `PointCloudView` (kamera orbitalna).

## Poza zakresem
Styl VeloView, nagrywanie, odtwarzanie, live, kolorowanie wg intensity.
