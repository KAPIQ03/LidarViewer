# Etap 4 - podglad na zywo po UDP - plan wdrozenia

> **Wykonanie:** zadania po kolei, kazde z testem najpierw (RED), potem implementacja (GREEN), na koniec commit.

**Cel:** odbior chmur punktow po UDP, kumulacja i podglad na zywo w oknie, z wbudowanym symulatorem.

**Architektura:** `LidarViewer.Core/Streaming` - dekodery, bufor, odbiorca UDP (bez Avalonii). `LidarViewer.App` - symulator, model widoku, pasek UI.

**Stack:** .NET 10, `System.Net.Sockets`, CommunityToolkit.Mvvm, Avalonia 12.

---

### Zadanie 1: Dekoder wlasnego protokolu LVLR

**Pliki:**
- Create: `src/LidarViewer.Core/Streaming/IPacketDecoder.cs`
- Create: `src/LidarViewer.Core/Streaming/SimpleCloudProtocol.cs`
- Create: `src/LidarViewer.Core/Streaming/SimpleCloudDecoder.cs`
- Test: `tests/LidarViewer.Core.Tests/Streaming/SimpleCloudDecoderTests.cs`

**Kroki:**
1. Test: `SimpleCloudProtocol.Encode` tworzy pakiet z naglowkiem 10 B i 13 B na punkt; `SimpleCloudDecoder.TryDecode` odtwarza te same wspolrzedne i intensywnosci.
2. Testy negatywne: zla magika, za krotki pakiet, niezgodna liczba punktow - `TryDecode` zwraca `false`.
3. Implementacja `IPacketDecoder`, `SimpleCloudProtocol`, `SimpleCloudDecoder`.
4. `dotnet test tests/LidarViewer.Core.Tests` - zielone.
5. Commit: `feat(core): dekoder wlasnego protokolu chmury punktow`

### Zadanie 2: Dekoder Velodyne VLP-16

**Pliki:**
- Create: `src/LidarViewer.Core/Streaming/VelodyneVlp16Decoder.cs`
- Test: `tests/LidarViewer.Core.Tests/Streaming/VelodyneVlp16DecoderTests.cs`

**Kroki:**
1. Test: pakiet zbudowany recznie (naglowek banku `0xEEFF`, azymut 0, dystans 1000 -> 2.0 m) daje punkt o oczekiwanych wspolrzednych.
2. Test: odleglosc 0 (brak powrotu) nie tworzy punktu; zly naglowek banku odrzuca pakiet.
3. Implementacja dekodera: 12 blokow x 100 B, azymut z offsetu +2, dane z offsetu +4, dystans * 0.002 m, katy pionowe z `VLP16db.yaml`, interpolacja azymutu w czasie strzalu.
4. Commit: `feat(core): dekoder pakietow velodyne vlp-16`

### Zadanie 3: Kumulujacy bufor chmury

**Pliki:**
- Create: `src/LidarViewer.Core/Streaming/LiveCloudBuffer.cs`
- Test: `tests/LidarViewer.Core.Tests/Streaming/LiveCloudBufferTests.cs`

**Kroki:**
1. Test: dodanie chmury kumuluje punkty; przekroczenie pojemnosci kasuje najstarsze; `Snapshot()` zwraca punkty w kolejnosci dodania.
2. Implementacja bufora cyklicznego (`LiveCloudBuffer`).
3. Commit: `feat(core): kumulujacy bufor punktow dla trybu live`

### Zadanie 4: Odbiorca UDP

**Pliki:**
- Create: `src/LidarViewer.Core/Streaming/UdpCloudReceiver.cs`
- Test: `tests/LidarViewer.Core.Tests/Streaming/UdpCloudReceiverTests.cs`

**Kroki:**
1. Test: wyslanie pakietu na loopback wywoluje zdarzenie z chmura o oczekiwanej liczbie punktow.
2. Test: pakiet zlych rozmiarow jest odrzucony (licznik odrzuceń rosnie), zdarzenie nie zachodzi.
3. Implementacja: `UdpClient` na `IPAddress.Any`, watk `ReceiveAsync` z tokenem, bufor, zdarzenie `CloudUpdated` maksymalnie 10 Hz.
4. Commit: `feat(core): odbiornik chmur punktow po udp`

### Zadanie 5: Symulator

**Pliki:**
- Create: `src/LidarViewer.App/Streaming/LiveSimulator.cs`
- Test: `tests/LidarViewer.App.Tests/LiveSimulatorTests.cs`

**Kroki:**
1. Test: symulator wysyla pakiety, ktore odbiornik dekoduje do chmury o oczekiwanej liczbie punktow.
2. Implementacja: watek co 50 ms buduje wiatrak 180 x 7 punktow i wysyla na `127.0.0.1:port`.
3. Commit: `feat(app): symulator strumienia punktow po udp`

### Zadanie 6: Model widoku - tryb live

**Pliki:**
- Create: `src/LidarViewer.App/ViewModels/LiveProtocol.cs`
- Modify: `src/LidarViewer.App/ViewModels/MainViewModel.cs`
- Test: `tests/LidarViewer.App.Tests/LiveModeTests.cs`

**Kroki:**
1. Test: `StartLiveCommand` uruchamia odbior (receiver nie null, `IsLive` true), `StopLiveCommand` zatrzymuje.
2. Test: chmura z odbiornika trafia do `PointCloud` po `ApplyLiveCloud`.
3. Test: zly port nie wywala aplikacji - `Status` zawiera komunikat bledu.
4. Implementacja: `LiveProtocol` (Wlasny / VelodyneVlp16), `LivePort`, `IsLive`, `LivePointCount`, komendy, zdarzenie `LiveCloudUpdated`.
5. Commit: `feat(app): tryb live w modelu widoku`

### Zadanie 7: Pasek live w oknie + blokada kamery

**Pliki:**
- Modify: `src/LidarViewer.App/Views/MainWindow.axaml`
- Modify: `src/LidarViewer.App/Views/MainWindow.axaml.cs`
- Modify: `src/LidarViewer.App/Controls/PointCloudView3D.cs`
- Modify: `src/LidarViewer.App/Controls/PointCloudView.cs`

**Kroki:**
1. Test: brak (zmiana widoku) - sprawdzenie kompilacji i zachowania `AutoFitCamera`/`AutoResetView`.
2. Implementacja: pasek "Live UDP" z polem portu, wyborem protokolu, przyciskami Start/Stop i checkboxem symulatora; przeniesienie chmury na watek UI przez `Dispatcher.UIThread.Post`; `AutoFitCamera`/`AutoResetView` = `!IsLive`.
3. Commit: `feat(app): pasek trybu live i stabilna kamera w trybie live`
