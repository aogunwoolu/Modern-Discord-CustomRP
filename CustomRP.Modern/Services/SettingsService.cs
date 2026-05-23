using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CustomRP.Modern.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public int DiscordPipe { get; set; } = -1;
    public string? LastClientId { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Optional Katsau API key for high-quality favicon resolution via HTML parsing.</summary>
    public string KatsauApiKey { get; set; } = "";

    /// <summary>
    /// Fallback Discord Application Client ID used when a preset doesn't specify
    /// its own. Set a unique ID per-preset in the editor to run multiple presences
    /// simultaneously — Discord shows one activity per Application ID.
    /// </summary>
    public string DefaultClientId { get; set; } = "896771305108553788";
}

/// <summary>
/// Persists user preferences as JSON under %APPDATA%/CustomRP.Modern.
/// </summary>
public sealed class SettingsService
{
    private readonly string _path;
    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CustomRP.Modern");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Current = Load();
    }

    public void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
