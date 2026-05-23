using CustomRP.Modern.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CustomRP.Modern.Services;

public sealed class RunningPreset
{
    public string DisplayName { get; set; } = "";
    public Preset Preset { get; set; } = new();
}

/// <summary>
/// Persists the set of currently-running presences to %APPDATA%/CustomRP.Modern/
/// active.json so they resume on the next launch — closing the app, even via
/// tray exit, doesn't lose the user's running setup.
/// </summary>
public sealed class ActivePresetsStore
{
    private readonly string _path;

    public ActivePresetsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CustomRP.Modern");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "active.json");
    }

    public List<RunningPreset> Load()
    {
        if (!File.Exists(_path)) return new List<RunningPreset>();
        try
        {
            return JsonSerializer.Deserialize<List<RunningPreset>>(
                File.ReadAllText(_path), JsonDefaults.Options) ?? new List<RunningPreset>();
        }
        catch { return new List<RunningPreset>(); }
    }

    public void Save(IEnumerable<RunningPreset> presets)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(presets, JsonDefaults.PresetOptions));
        }
        catch { /* best effort */ }
    }
}
