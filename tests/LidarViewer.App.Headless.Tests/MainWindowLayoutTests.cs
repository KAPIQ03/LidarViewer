using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
}
