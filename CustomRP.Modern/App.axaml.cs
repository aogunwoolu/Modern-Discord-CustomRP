using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CustomRP.Modern.Services;
using CustomRP.Modern.ViewModels;
using CustomRP.Modern.Views;

namespace CustomRP.Modern;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = new AppServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(Services),
            };

            desktop.ShutdownRequested += (_, _) => Services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class AppServices : System.IDisposable
{
    public ConnectionManager Connections { get; }
    public PresetService Presets { get; } = new();
    public KnownAppsRegistry KnownApps { get; } = new();
    public AppDetectionService Detection { get; }
    public SettingsService Settings { get; } = new();
    public FaviconService Favicons { get; } = new();
    public AutoUpdateService AutoUpdate { get; }

    public AppServices()
    {
        Connections = new ConnectionManager(Settings);
        Detection = new AppDetectionService(KnownApps);
        AutoUpdate = new AutoUpdateService(Favicons);
    }

    public void Dispose()
    {
        AutoUpdate.Dispose();
        Connections.Dispose();
    }
}
