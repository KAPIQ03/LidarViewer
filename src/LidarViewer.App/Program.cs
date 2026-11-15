using Avalonia;
using System;

namespace LidarViewer.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    private static AvaloniaNativePlatformOptions CreateMacOptions() => new()
    {
        RenderingMode = OperatingSystem.IsMacOS()
            ? [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software]
            : new AvaloniaNativePlatformOptions().RenderingMode,
    };

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
