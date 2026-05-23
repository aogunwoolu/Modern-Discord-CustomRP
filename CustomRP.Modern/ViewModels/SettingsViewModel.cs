using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomRP.Modern.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace CustomRP.Modern.ViewModels;

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

    public IReadOnlyList<string> Themes { get; } = new[] { "Dark", "Light", "System" };

    [ObservableProperty] private string _theme;
    [ObservableProperty] private int _discordPipe;
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private string _presetsDirectory;
    [ObservableProperty] private string _discordLogPath;
    [ObservableProperty] private string _startupLogPath;

    public ObservableCollection<CategoryClientIdEntry> CategoryClientIds { get; } = new();

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        var s = services.Settings.Current;
        _theme = s.Theme;
        _discordPipe = s.DiscordPipe;
        _autoReconnect = s.AutoReconnect;
        _minimizeToTray = s.MinimizeToTray;
        _presetsDirectory = services.Presets.UserPresetsDirectory;
        _discordLogPath = DiscordFileLogger.DefaultPath;
        _startupLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomRP.Modern", "startup.log");

        foreach (var (cat, id) in s.CategoryClientIds)
            CategoryClientIds.Add(new CategoryClientIdEntry(cat, id));
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        var dir = Path.GetDirectoryName(DiscordLogPath)!;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
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
        s.Theme = Theme;
        s.DiscordPipe = DiscordPipe;
        s.AutoReconnect = AutoReconnect;
        s.MinimizeToTray = MinimizeToTray;

        foreach (var entry in CategoryClientIds)
            s.CategoryClientIds[entry.Category] = entry.ClientId.Trim();

        _services.Settings.Save();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (Avalonia.Application.Current is null) return;
        Avalonia.Application.Current.RequestedThemeVariant = Theme switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark"  => Avalonia.Styling.ThemeVariant.Dark,
            _       => Avalonia.Styling.ThemeVariant.Default,
        };
    }
}
