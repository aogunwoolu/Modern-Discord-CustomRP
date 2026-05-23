using Avalonia.Platform;
using CustomRP.Modern.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CustomRP.Modern.Services;

/// <summary>
/// Loads and saves presets. New format is JSON (.crpreset); legacy XML (.crp)
/// from CustomRP 1.x is supported on import only.
/// </summary>
public sealed class PresetService
{
    public const string NativeExtension = ".crpreset";
    public const string LegacyExtension = ".crp";

    public string UserPresetsDirectory { get; }

    public PresetService()
    {
        UserPresetsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CustomRP.Modern", "presets");
        Directory.CreateDirectory(UserPresetsDirectory);
    }

    public async Task<Preset> LoadAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == LegacyExtension)
            return LoadLegacyXml(filePath);

        await using var stream = File.OpenRead(filePath);
        var preset = await JsonSerializer.DeserializeAsync<Preset>(stream, JsonDefaults.PresetOptions)
                     ?? throw new InvalidDataException("Empty preset file.");
        preset.Metadata ??= new PresetMetadata();
        preset.Buttons ??= new List<PresenceButton> { new(), new() };
        while (preset.Buttons.Count < 2) preset.Buttons.Add(new PresenceButton());
        return preset;
    }

    public async Task SaveAsync(Preset preset, string filePath)
    {
        preset.Metadata.Modified = DateTime.UtcNow;
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, preset, JsonDefaults.PresetOptions);
    }

    private static readonly string[] BundledPresetFiles =
    {
        "getting-started.crpreset",
        "listening-to-music.crpreset",
        "gaming-session.crpreset",
    };

    public IReadOnlyList<PresetEntry> ListBundledPresets()
    {
        var results = new List<PresetEntry>();
        foreach (var file in BundledPresetFiles)
        {
            try
            {
                var uri = new Uri($"avares://CustomRP.Modern/Assets/Presets/{file}");
                using var stream = AssetLoader.Open(uri);
                var preset = JsonSerializer.Deserialize<Preset>(stream, JsonDefaults.PresetOptions);
                if (preset is null) continue;
                NormalizePreset(preset);
                results.Add(new PresetEntry(uri.AbsoluteUri, preset, IsBundled: true,
                    DisplayFileName: Path.GetFileNameWithoutExtension(file)));
            }
            catch { /* skip broken bundled file */ }
        }
        return results.OrderBy(e => e.Preset.Metadata.Name).ToList();
    }

    public IReadOnlyList<PresetEntry> ListUserPresets()
    {
        var results = new List<PresetEntry>();
        foreach (var file in Directory.EnumerateFiles(UserPresetsDirectory, "*" + NativeExtension))
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<Preset>(json, JsonDefaults.PresetOptions);
                if (preset is null) continue;
                NormalizePreset(preset);
                results.Add(new PresetEntry(file, preset));
            }
            catch
            {
                // Skip unreadable presets — never crash the library view.
            }
        }
        return results
            .OrderByDescending(e => e.Preset.Metadata.Modified)
            .ToList();
    }

    private static void NormalizePreset(Preset preset)
    {
        preset.Metadata ??= new PresetMetadata();
        preset.Buttons ??= new List<PresenceButton> { new(), new() };
        while (preset.Buttons.Count < 2) preset.Buttons.Add(new PresenceButton());
    }

    public string SuggestFileName(Preset preset)
    {
        var safe = string.Concat((preset.Metadata.Name ?? "preset")
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(safe)) safe = "preset";
        return safe + NativeExtension;
    }

    // ---- Legacy XML support ----

    [Serializable]
    public struct LegacyPreset
    {
        public string ID;
        public int Type;
        public int Display;
        public string Name;
        public string Details;
        public string DetailsURL;
        public string State;
        public string StateURL;
        public int PartySize;
        public int PartyMax;
        public int Timestamps;
        public DateTime CustomTimestamp;
        public bool CustomTimestampEndEnabled;
        public DateTime CustomTimestampEnd;
        public string LargeKey;
        public string LargeText;
        public string LargeURL;
        public string SmallKey;
        public string SmallText;
        public string SmallURL;
        public string Button1Text;
        public string Button1URL;
        public string Button2Text;
        public string Button2URL;
    }

    private static Preset LoadLegacyXml(string filePath)
    {
        var xs = new XmlSerializer(typeof(LegacyPreset));
        using var stream = File.OpenRead(filePath);
        var l = (LegacyPreset)xs.Deserialize(stream)!;

        return new Preset
        {
            ClientId = l.ID ?? "",
            Type = (ActivityKind)l.Type,
            Display = (DisplayMode)l.Display,
            ActivityName = l.Name ?? "",
            Details = l.Details ?? "",
            DetailsUrl = l.DetailsURL ?? "",
            State = l.State ?? "",
            StateUrl = l.StateURL ?? "",
            PartySize = l.PartySize,
            PartyMax = l.PartyMax,
            Timestamps = (TimestampMode)Math.Min(l.Timestamps, (int)TimestampMode.Custom),
            CustomTimestampStart = l.CustomTimestamp == default ? DateTime.UtcNow : l.CustomTimestamp,
            CustomTimestampEndEnabled = l.CustomTimestampEndEnabled,
            CustomTimestampEnd = l.CustomTimestampEnd == default ? DateTime.UtcNow.AddHours(1) : l.CustomTimestampEnd,
            LargeImage = new ImageAsset { Key = l.LargeKey ?? "", Text = l.LargeText ?? "", Url = l.LargeURL ?? "" },
            SmallImage = new ImageAsset { Key = l.SmallKey ?? "", Text = l.SmallText ?? "", Url = l.SmallURL ?? "" },
            Buttons = new List<PresenceButton>
            {
                new() { Text = l.Button1Text ?? "", Url = l.Button1URL ?? "" },
                new() { Text = l.Button2Text ?? "", Url = l.Button2URL ?? "" },
            },
            Metadata = new PresetMetadata
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Description = "Imported from CustomRP 1.x",
                Tags = new List<string> { "imported" },
            },
        };
    }
}

public sealed record PresetEntry(
    string FilePath,
    Preset Preset,
    bool IsBundled = false,
    string? DisplayFileName = null);
