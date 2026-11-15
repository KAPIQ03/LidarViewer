using Avalonia;
using Avalonia.Headless;
using LidarViewer.App;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(LidarViewer.App.Headless.Tests.TestAppBuilder))]

namespace LidarViewer.App.Headless.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
