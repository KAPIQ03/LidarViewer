using Avalonia;
using System;

namespace LidarViewer.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Na macOS Avalonia domyślnie renderuje przez Metal (kolejność Metal -> OpenGl -> Software),
    /// a kontrolka OpenGlControlBase potrzebuje trybu OpenGl - przy Metalu nie dostaje kontekstu GL
    /// i nic nie rysuje. Dlatego na macOS wymuszamy OpenGl (z Software jako zapasowym).
    /// Na innych systemach ustawienie jest ignorowane.
    /// </summary>
    private static AvaloniaNativePlatformOptions CreateMacOptions() => new()
    {
        RenderingMode = OperatingSystem.IsMacOS()
            ? [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software]
            : new AvaloniaNativePlatformOptions().RenderingMode,
    };

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(CreateMacOptions())
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
