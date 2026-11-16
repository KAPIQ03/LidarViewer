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

    [AvaloniaFact]
    public void RecordButtonAppearsOnlyInLiveMode()
    {
        var vm = new MainViewModel { ViewMode = ViewMode.Widok2D, LivePort = 34811, LiveProtocol = LiveProtocol.VelodyneVlp16 };
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

    [AvaloniaFact]
    public void SavesScreenshotOfWindowWithData()
    {
        {
            var (window, vm) = ShowWindow();
            var rnd = new Random(1);
            var positions = new float[3 * 3000];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = (float)(rnd.NextDouble() * 20 - 10);
            vm.SetSourceForTests(new LidarViewer.Core.Models.PointCloud(positions));
            vm.StartPlayback(new FakeFrameSource(5));
            vm.IsTableOpen = true;
            vm.ColorMode = LidarViewer.Core.Rendering.ColorMode.Height;
            Dispatcher.UIThread.RunJobs();

            WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            using (FileStream stream = File.Create(Path.Combine(AppContext.BaseDirectory, "main-window-data.png")))
                frame!.Save(stream, new PngBitmapEncoderOptions());
            window.Close();
        }
    }

    private static Rect ToWindow(Control control, Window window)
    {
        Point origin = control.TranslatePoint(new Point(0, 0), window) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    private sealed class FakeFrameSource(int count) : LidarViewer.Core.Playback.IFrameSource
    {
        public string Name => "test";

        public int FrameCount => count;

        public IReadOnlyList<double> Times { get; } = [.. Enumerable.Range(0, count).Select(i => i * 0.1)];

        public double Duration => Times[^1];

        public int FrameAt(double positionSeconds) =>
            Math.Clamp((int)Math.Floor(positionSeconds / 0.1 + 1e-9), 0, count - 1);

        public LidarViewer.Core.Models.PointCloud LoadFrame(int index) => new([0f, 0f, 0f]);
    }

    [AvaloniaFact]
    public void PlaybackBarShowsFrameNumbersAndFitsWindow()
    {
        {
            var (window, vm) = ShowWindow();
            vm.StartPlayback(new FakeFrameSource(4));
            Dispatcher.UIThread.RunJobs();

            var boxes = window.GetVisualDescendants().OfType<NumericUpDown>().ToList();
            Assert.Equal(4, boxes.Count);
            Assert.Equal(1m, boxes[0].Value);
            Assert.Equal(1m, boxes[1].Value);
            Assert.Equal(4m, boxes[2].Value);
            Assert.Equal(0m, boxes[3].Value);

            vm.StepForwardCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2m, boxes[0].Value);

            foreach (var box in boxes)
                Assert.True(ToWindow(box, window).Right <= Width, "pole poza oknem");
        }
    }

    [AvaloniaFact]
    public void FrameNumberBoxShowsWholeNumber()
    {
        var (window, vm) = ShowWindow();
        vm.StartPlayback(new FakeFrameSource(99999));
        vm.LastFrameCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var boxes = window.GetVisualDescendants().OfType<NumericUpDown>().ToList();
        Assert.Equal(99999m, boxes[0].Value);
        var frameText = boxes[0].GetVisualDescendants().OfType<TextBox>().First();
        Assert.Equal(Avalonia.Media.TextAlignment.Center, frameText.TextAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, frameText.VerticalContentAlignment);
        foreach (var box in boxes)
        {
            var scroll = box.GetVisualDescendants().OfType<TextBox>().First()
                .GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5,
                $"Liczba {box.Value} nie miesci sie w polu: tekst {scroll.Extent.Width}, pole {scroll.Viewport.Width}.");
        }

        window.Close();
    }

    [AvaloniaFact]
    public void PointTableIsVirtualized()
    {
        var (window, vm) = ShowWindow();
        var positions = new float[3 * 50_000];
        vm.SetSourceForTests(new LidarViewer.Core.Models.PointCloud(positions));

        vm.IsTableOpen = true;
        Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.ItemsSource is LidarViewer.Core.Models.PointTable);
        Assert.Equal(50_000, list.ItemCount);
        int realized = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
        Assert.InRange(realized, 1, 200);
    }

    [AvaloniaFact]
    public void MenuBarHasVeloViewStyleSections()
    {
        var (window, _) = ShowWindow();

        var menu = window.GetVisualDescendants().OfType<Menu>().Single();
        var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();

        Assert.Equal(["_Plik", "_Widok", "_Live", "_Narzędzia", "_Pomoc"], headers);
    }

    [AvaloniaFact]
    public void MenuCheckItemsWriteBackToViewModel()
    {
        var (window, vm) = ShowWindow();
        var items = window.GetVisualDescendants().OfType<Menu>().Single()
            .Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()).ToList();

        MenuItem grid = items.Single(m => m.Header?.ToString() == "Siatka");
        bool before = vm.ShowGrid;
        grid.IsChecked = !before;
        Assert.Equal(!before, vm.ShowGrid);

        MenuItem log = items.Single(m => m.Header?.ToString() == "Log diagnostyczny");
        log.IsChecked = true;
        Assert.True(vm.IsDiagnosticsOpen);

        window.Close();
    }

    [AvaloniaFact]
    public void ViewCubeIsVisibleOnlyIn3DMode()
    {
        var (window, vm) = ShowWindow();

        vm.ViewMode = ViewMode.Widok3D;
        Assert.True(vm.ShowViewCube);
        vm.ViewMode = ViewMode.Widok2D;
        Assert.False(vm.ShowViewCube);

        window.Close();
    }
}
