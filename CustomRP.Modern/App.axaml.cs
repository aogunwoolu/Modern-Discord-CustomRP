using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using CustomRP.Modern.Services;
using CustomRP.Modern.ViewModels;
using CustomRP.Modern.Views;
using System;
using System.Linq;

namespace CustomRP.Modern;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    /// <summary>Set when the user picks Exit from the tray menu — tells the
    /// window's Closing handler to actually close instead of hiding to tray.</summary>
    public static bool IsExiting { get; private set; }

    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = new AppServices();
        ApplyTheme(Services.Settings.Current.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Restore connections FIRST so that PresenceEditorViewModel.LoadPreset
            // can adopt them immediately via Find() — no ConnectionAdded race needed.
            foreach (var entry in Services.ActivePresets.Load())
                Services.Connections.Start(entry.Preset, entry.DisplayName);

            _mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(Services),
            };
            desktop.MainWindow = _mainWindow;

            SetupTrayIcon(desktop);

            desktop.ShutdownRequested += (_, _) => Services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://CustomRP.Modern/Assets/app.ico"));
        var icon = new WindowIcon(iconStream);

        var menu = new NativeMenu();
        var showItem = new NativeMenuItem { Header = "Show CustomRP" };
        showItem.Click += (_, _) => ShowWindow();
        var exitItem = new NativeMenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            IsExiting = true;
            desktop.Shutdown();
        };
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "CustomRP — Discord Rich Presence",
            IsVisible = true,
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    /// <summary>
    /// Applies the named theme. Built-in variants (Dark/Light/System) use Avalonia's
    /// ThemeDictionaries; custom themes additionally merge an override ResourceDictionary.
    /// </summary>
    public static void ApplyTheme(string theme)
    {
        var app = Current;
        if (app is null) return;

        app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark"  => ThemeVariant.Dark,
            _       => ThemeVariant.Dark,
        };

        var merged = app.Resources.MergedDictionaries;
        var existing = merged.OfType<ResourceInclude>()
            .FirstOrDefault(r => r.Source?.ToString().Contains("/Styles/Themes/") == true);
        if (existing is not null)
            merged.Remove(existing);

        if (theme is "Dark" or "Light" or "System") return;

        merged.Add(new ResourceInclude((Uri?)null)
        {
            Source = new Uri($"avares://CustomRP.Modern/Styles/Themes/{theme}.axaml"),
        });
    }

    private void ShowWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }
}

public sealed class AppServices : System.IDisposable
{
    public ConnectionManager Connections { get; }
    public PresetService Presets { get; } = new();
    public KnownAppsRegistry KnownApps { get; } = new();
    public AppDetectionService Detection { get; }
    public SettingsService Settings { get; } = new();
    public FaviconService Favicons { get; }
    public AutoUpdateService AutoUpdate { get; }
    public ActivePresetsStore ActivePresets { get; } = new();

    private bool _disposing;

    public AppServices()
    {
        Favicons = new FaviconService(Settings);
        Connections = new ConnectionManager(Settings);
        Detection = new AppDetectionService(KnownApps);
        AutoUpdate = new AutoUpdateService(Favicons);

        // Snapshot active presences to disk whenever the set changes so the
        // next launch resumes them. Skipped during shutdown — otherwise the
        // StopAll cascade would write an empty list right before exit.
        Connections.ConnectionAdded += (_, _) => SnapshotActive();
        Connections.ConnectionRemoved += (_, _) => SnapshotActive();
    }

    private void SnapshotActive()
    {
        if (_disposing) return;
        ActivePresets.Save(Connections.Connections.Select(c => new RunningPreset
        {
            DisplayName = c.DisplayName,
            Preset = c.Preset,
        }));
    }

    public void Dispose()
    {
        // Write the current state of all live connections before tearing anything
        // down. Without this, image-key edits pushed to Discord (which update
        // conn.Preset in memory) are lost on restart because SnapshotActive only
        // fires on connection-add/remove events, not on every presence update.
        SnapshotActive();
        _disposing = true;
        AutoUpdate.Dispose();
        Connections.Dispose();
    }
}
