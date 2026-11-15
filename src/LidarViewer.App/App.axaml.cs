using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LidarViewer.App.ViewModels;
using LidarViewer.App.Views;

namespace LidarViewer.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel { SimulatorPcapPath = FindSamplePcap() },
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private const string SamplePcapRelativePath =
        "lidar-samples/VLP-16 Sample Data/2015-07-23-14-37-22_Velodyne-VLP-16-Data_Downtown 10Hz Single.pcap";

    private static string? FindSamplePcap()
    {
        for (var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, SamplePcapRelativePath);
            if (System.IO.File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
