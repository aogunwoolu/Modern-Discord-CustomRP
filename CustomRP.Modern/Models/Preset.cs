using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CustomRP.Modern.Models;

public sealed class Preset
{
    public int SchemaVersion { get; set; } = 1;

    public PresetMetadata Metadata { get; set; } = new();

    public string ClientId { get; set; } = "";

    public ActivityKind Type { get; set; } = ActivityKind.Playing;
    public DisplayMode Display { get; set; } = DisplayMode.Name;

    public string ActivityName { get; set; } = "";
    public string Details { get; set; } = "";
    public string DetailsUrl { get; set; } = "";
    public string State { get; set; } = "";
    public string StateUrl { get; set; } = "";

    public int PartySize { get; set; }
    public int PartyMax { get; set; }

    public TimestampMode Timestamps { get; set; } = TimestampMode.None;
    public DateTime CustomTimestampStart { get; set; } = DateTime.UtcNow;
    public bool CustomTimestampEndEnabled { get; set; }
    public DateTime CustomTimestampEnd { get; set; } = DateTime.UtcNow.AddHours(1);

    public ImageAsset LargeImage { get; set; } = new();
    public ImageAsset SmallImage { get; set; } = new();

    public List<PresenceButton> Buttons { get; set; } = new() { new(), new() };

    public AutoUpdateConfig AutoUpdate { get; set; } = new();
}

public sealed class PresetMetadata
{
    public string Name { get; set; } = "Untitled preset";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string IconUrl { get; set; } = "";
    /// <summary>Matches one of the known-app category strings (e.g. "Games", "Browsers").
    /// Used to look up the per-category Discord Application Client ID from Settings.</summary>
    public string Category { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;
}

public sealed class ImageAsset
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class PresenceButton
{
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && string.IsNullOrWhiteSpace(Url);
}

public enum ActivityKind
{
    Playing = 0,
    Listening = 2,
    Watching = 3,
    Competing = 5,
}

public enum DisplayMode
{
    Name = 0,
    State = 1,
    Details = 2,
}

public enum TimestampMode
{
    None,
    SinceStart,
    LocalTime,
    Custom,
}
