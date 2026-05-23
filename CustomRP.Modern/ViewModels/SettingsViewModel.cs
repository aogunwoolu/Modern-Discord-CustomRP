using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomRP.Modern.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.IO;

namespace CustomRP.Modern.ViewModels;

/// <summary>One entry in the theme picker — carries a display name and hex accent colour for the swatch.</summary>
public sealed record ThemeOption(string Name, string Accent)
{
    public override string ToString() => Name;
}

/// <summary>One editable row in the "Discord Applications" settings card.</summary>
public partial class CategoryClientIdEntry : ObservableObject
{
    public string Category { get; }
    public string CategoryIcon { get; }
    [ObservableProperty] private string _clientId;

    public CategoryClientIdEntry(string category, string clientId)
    {
        Category = category;
        _clientId = clientId;
        CategoryIcon = category switch
        {
            "Browsers"      => "🌐",
            "Communication" => "💬",
            "Creative"      => "🎨",
            "Development"   => "💻",
            "Games"         => "🎮",
            "Launchers"     => "🚀",
            "Music"         => "🎵",
            "Productivity"  => "📋",
            "Video"         => "📺",
            _               => "📦",
        };
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public IReadOnlyList<ThemeOption> Themes { get; } = new ThemeOption[]
    {
        new("Dark",     "#5865F2"),
        new("Light",    "#5865F2"),
        new("System",   "#8B8B8B"),
        new("Midnight", "#7C4DFF"),
        new("Ocean",    "#00BCD4"),
        new("Rose",     "#E91E8C"),
        new("Forest",   "#00C853"),
        new("Nord",     "#5E81AC"),
    };

    [ObservableProperty] private ThemeOption _selectedTheme = null!;
    [ObservableProperty] private int _discordPipe;
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private string _katsauApiKey = "";
    [ObservableProperty] private string _presetsDirectory;
    [ObservableProperty] private string _discordLogPath;
    [ObservableProperty] private string _startupLogPath;

    public ObservableCollection<CategoryClientIdEntry> CategoryClientIds { get; } = new();

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        var s = services.Settings.Current;
        _selectedTheme = Themes.FirstOrDefault(t => t.Name == s.Theme) ?? Themes[0];
        _discordPipe = s.DiscordPipe;
        _autoReconnect = s.AutoReconnect;
        _minimizeToTray = s.MinimizeToTray;
        _katsauApiKey = s.KatsauApiKey ?? "";
        _presetsDirectory = services.Presets.UserPresetsDirectory;
        _discordLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomRP.Modern", "workers");
        _startupLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomRP.Modern", "startup.log");

        foreach (var (cat, id) in s.CategoryClientIds)
            CategoryClientIds.Add(new CategoryClientIdEntry(cat, id));
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(DiscordLogPath);
        Process.Start(new ProcessStartInfo { FileName = DiscordLogPath, UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenDevPortal() =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://discord.com/developers/applications",
            UseShellExecute = true,
        });

    [RelayCommand]
    private void Save()
    {
        var s = _services.Settings.Current;
        s.Theme = SelectedTheme?.Name ?? "Dark";
        s.DiscordPipe = DiscordPipe;
        s.AutoReconnect = AutoReconnect;
        s.MinimizeToTray = MinimizeToTray;
        s.KatsauApiKey = KatsauApiKey.Trim();

        foreach (var entry in CategoryClientIds)
            s.CategoryClientIds[entry.Category] = entry.ClientId.Trim();

        _services.Settings.Save();
        ApplyTheme();
    }

    private void ApplyTheme() => App.ApplyTheme(SelectedTheme?.Name ?? "Dark");
}
