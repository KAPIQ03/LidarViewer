using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LidarViewer.App.ViewModels;
using LidarViewer.App.Views;

namespace LidarViewer.App.Headless.Tests;

public class MainWindowLayoutTests
{
    private const double Width = 1100;
    private const double Height = 700;

    private static (MainWindow Window, MainViewModel Vm) ShowWindow(double width = Width, double height = Height)
    {
        var vm = new MainViewModel { ViewMode = ViewMode.Widok2D };
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    // Pasek narzedzi musi lezec nad panelem filtrow. Wczesniej oba byly
    // w jednej kolumnie, wiec szeroki pasek live chowal sie pod panelem.
    [AvaloniaFact]
    public void ToolbarStaysAboveFiltersPanel()
    {
        var (window, _) = ShowWindow();

        var toolbar = window.FindControl<StackPanel>("Toolbar");
        var panel = window.FindControl<ScrollViewer>("FiltersPanel");

        Assert.NotNull(toolbar);
        Assert.NotNull(panel);

        Rect toolbarRect = ToWindow(toolbar!, window);
        Rect panelRect = ToWindow(panel!, window);

        Assert.True(
            toolbarRect.Bottom <= panelRect.Top,
            $"Pasek narzedzi ({toolbarRect}) naklada sie na panel filtrow ({panelRect}).");

        window.Close();
    }

    // Zawartosc paska nie moze wychodzic poza okno - inaczej elementy
    // znikaja pod panelem filtrow.
    [AvaloniaFact]
    public void ToolbarFitsInsideWindow()
    {
        var (window, _) = ShowWindow();

        var toolbar = window.FindControl<StackPanel>("Toolbar");
        Assert.NotNull(toolbar);

        Rect toolbarRect = ToWindow(toolbar!, window);

        Assert.True(toolbarRect.Right <= Width, $"Pasek narzedzi wychodzi poza okno: {toolbarRect}.");
        Assert.True(toolbarRect.Bottom <= Height, $"Pasek narzedzi wychodzi poza okno: {toolbarRect}.");

        window.Close();
    }

    // Wiersz live musi zawijac sie do kolejnego wiersza, a nie wychodzic
    // poza szerokosc okna.
    [AvaloniaFact]
    public void LiveRowWrapsInsideWindow()
    {
        var (window, _) = ShowWindow();

        var liveRow = window.FindControl<WrapPanel>("LiveRow");
        Assert.NotNull(liveRow);

        Rect rowRect = ToWindow(liveRow!, window);

        Assert.True(rowRect.Right <= Width, $"Wiersz live wychodzi poza okno: {rowRect}.");

        foreach (Control child in liveRow!.Children)
        {
            Rect childRect = ToWindow(child, window);
            Assert.True(
                childRect.Right <= Width && childRect.Bottom <= Height,
                $"Element wiersza live wychodzi poza okno: {childRect}.");
        }

        window.Close();
    }

    // Wezsokie okno nie moze byc miejscem, w ktorym wszystko sie nie
    // mieści - elementy musza sie zawijać na kolejne wiersze.
    [AvaloniaFact]
    public void ToolbarWrapsInNarrowWindow()
    {
        const double narrow = 800;
        var (window, _) = ShowWindow(narrow, 600);

        var toolbar = window.FindControl<StackPanel>("Toolbar");
        var liveRow = window.FindControl<WrapPanel>("LiveRow");
        Assert.NotNull(toolbar);
        Assert.NotNull(liveRow);

        Rect toolbarRect = ToWindow(toolbar!, window);

        Assert.True(toolbarRect.Right <= narrow, $"Pasek narzedzi wychodzi poza waskie okno: {toolbarRect}.");

        foreach (Control child in liveRow!.Children)
        {
            Rect childRect = ToWindow(child, window);
            Assert.True(
                childRect.Right <= narrow,
                $"Element wiersza live wychodzi poza waskie okno: {childRect}.");
        }

        window.Close();
    }

    // Panel filtrow mozna zamknac, zeby nie zaslanial widoku chmury.
    [AvaloniaFact]
    public void ClosingFiltersPanelFreesSpace()    {
        var (window, vm) = ShowWindow();

        var panel = window.FindControl<ScrollViewer>("FiltersPanel");
        Assert.NotNull(panel);
        Assert.True(panel!.IsVisible);

        vm.IsFiltersPanelOpen = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(panel.IsVisible);

        window.Close();
    }

    // Przyciski zapisu i nagrywania musza byc w pasku narzedzi i miescic
    // sie w szerokosci okna.
    [AvaloniaFact]
    public void ExportAndRecordButtonsFitInToolbar()
    {
        var (window, _) = ShowWindow();

        var save = window.FindControl<Button>("SaveAsButton");
        var record = window.FindControl<Button>("RecordButton");
        var stop = window.FindControl<Button>("StopRecordButton");

        Assert.NotNull(save);
        Assert.NotNull(record);
        Assert.NotNull(stop);

        foreach (Button button in new[] { save!, record!, stop! })
        {
            Rect rect = ToWindow(button, window);
            Assert.True(rect.Right <= Width, $"Przycisk wychodzi poza okno: {rect}.");
        }

        window.Close();
    }

    // Przycisk nagrywania pojawia sie dopiero w trybie live, bo bez
    // strumienia nie ma czego nagrywac.
    [AvaloniaFact]
    public void RecordButtonAppearsOnlyInLiveMode()
    {
        var vm = new MainViewModel { ViewMode = ViewMode.Widok2D, LivePort = 34811 };
        var window = new MainWindow { DataContext = vm, Width = Width, Height = Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var record = window.FindControl<Button>("RecordButton");
        var stop = window.FindControl<Button>("StopRecordButton");
        Assert.NotNull(record);
        Assert.NotNull(stop);

        Assert.False(record!.IsVisible);
        Assert.False(stop!.IsVisible);

        vm.StartLiveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(record.IsVisible);

        Rect rect = ToWindow(record, window);
        Assert.True(rect.Right <= Width, $"Przycisk nagrywania wychodzi poza okno: {rect}.");

        window.Close();
        vm.StopLiveCommand.Execute(null);
    }

    // Zrzut okna do pliku - pozwala ocenic wyglad bez uruchamiania
    // aplikacji na desktopie.
    [AvaloniaFact]
    public void SavesScreenshotOfWindow()
    {
        var vm = new MainViewModel { ViewMode = ViewMode.Widok2D };
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        string path = Path.Combine(AppContext.BaseDirectory, "main-window.png");

        using (FileStream stream = File.Create(path))
            frame!.Save(stream, new PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 0);

        window.Close();
    }

    private static Rect ToWindow(Control control, Window window)
    {
        Point origin = control.TranslatePoint(new Point(0, 0), window) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    private static string WriteSequence(int frames)
    {
        string dir = Path.Combine(Path.GetTempPath(), "lvhl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var entries = new List<string>();
        for (int i = 0; i < frames; i++)
        {
            File.WriteAllLines(Path.Combine(dir, $"f{i}.ply"),
            [
                "ply", "format ascii 1.0", "element vertex 1",
                "property float x", "property float y", "property float z", "end_header", "0 0 0",
            ]);
            entries.Add($"{{\"file\":\"f{i}.ply\",\"timestamp\":{i * 0.1}}}");
        }

        File.WriteAllText(Path.Combine(dir, "sequence.json"), "{\"frames\":[" + string.Join(",", entries) + "]}");
        return dir;
    }

    // Pasek odtwarzania: pola numeryczne musza pokazywac numer klatki i zakres, a caly pasek miesci sie w oknie.
    [AvaloniaFact]
    public void PlaybackBarShowsFrameNumbersAndFitsWindow()
    {
        string dir = WriteSequence(4);
        try
        {
            var (window, vm) = ShowWindow();
            vm.LoadSequenceCommand.Execute(dir);
            Dispatcher.UIThread.RunJobs();

            var boxes = window.GetVisualDescendants().OfType<NumericUpDown>().ToList();
            Assert.Equal(4, boxes.Count);
            Assert.Equal(1m, boxes[0].Value);   // numer klatki
            Assert.Equal(1m, boxes[1].Value);   // poczatek zakresu
            Assert.Equal(4m, boxes[2].Value);   // koniec zakresu
            Assert.Equal(0m, boxes[3].Value);   // slad

            vm.StepForwardCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2m, boxes[0].Value);

            foreach (var box in boxes)
                Assert.True(ToWindow(box, window).Right <= Width, "pole poza oknem");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
