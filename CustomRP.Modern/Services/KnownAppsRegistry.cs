using Avalonia.Platform;
using CustomRP.Modern.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CustomRP.Modern.Services;

/// <summary>
/// Loads the bundled list of well-known applications with their default
/// Discord client IDs and process-name hints. Used both by the preset
/// library and by the auto-detect scanner.
/// </summary>
public sealed class KnownAppsRegistry
{
    public IReadOnlyList<KnownApp> Apps { get; }
    public string? LoadError { get; }

    public KnownAppsRegistry()
    {
        (Apps, LoadError) = LoadBundled();
    }

    public IEnumerable<KnownApp> ByCategory(string category) =>
        Apps.Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> Categories =>
        Apps.Select(a => a.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c);

    public KnownApp? MatchProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        var bare = StripExe(processName);
        return Apps.FirstOrDefault(a =>
            a.ProcessNames.Any(p =>
                string.Equals(StripExe(p), bare, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Strips a trailing .exe only — avoids mangling names like "battle.net".</summary>
    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    private static (IReadOnlyList<KnownApp> Apps, string? Error) LoadBundled()
    {
        try
        {
            var uri = new Uri("avares://CustomRP.Modern/Assets/known-apps.json");
            using var stream = AssetLoader.Open(uri);
            var apps = JsonSerializer.Deserialize<List<KnownApp>>(stream, JsonDefaults.Options)
                       ?? new List<KnownApp>();
            foreach (var app in apps)
            {
                var resolved = AppIconService.Resolve(app);
                if (!string.IsNullOrEmpty(resolved))
                    app.IconUrl = resolved;
            }
            return (apps, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<KnownApp>(), ex.Message);
        }
    }
}
