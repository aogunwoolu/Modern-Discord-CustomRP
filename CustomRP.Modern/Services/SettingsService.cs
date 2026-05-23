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

    /// <summary>
    /// One Discord Application Client ID per known-app category.
    /// Replace each placeholder with a dedicated Discord Application you create at
    /// https://discord.com/developers/applications so multiple categories can run
    /// simultaneously without overwriting each other.
    /// </summary>
    public Dictionary<string, string> CategoryClientIds { get; set; } = new()
    {
        ["Browsers"]      = "1507440289390919882",
        ["Communication"] = "1507794956465475805",
        ["Creative"]      = "1507795285836038267",
        ["Development"]   = "1507795526610059386",
        ["Games"]         = "1507795792591589436",
        ["Launchers"]     = "1507796028361932830",
        ["Music"]         = "1507796396378554578",
        ["Productivity"]  = "896771305108553788",
        ["Video"]         = "1507796978992418908",
    };
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
