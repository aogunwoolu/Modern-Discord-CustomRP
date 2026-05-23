using System.Collections.Generic;

namespace CustomRP.Modern.Models;

/// <summary>
/// A curated app entry. Carries everything we know about how to surface this
/// app as a Discord rich presence: process detection, default preset values,
/// any pre-made scenarios, and links to where the user can fetch their own
/// Client ID.
/// </summary>
public sealed class KnownApp
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string? ClientId { get; set; }
    public List<string> ProcessNames { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    // ---- Default preset values applied when starting from this app ----
    public ActivityKind DefaultActivityType { get; set; } = ActivityKind.Playing;
    public string DefaultLargeImageKey { get; set; } = "";
    public string DefaultLargeImageText { get; set; } = "";
    public string DefaultSmallImageKey { get; set; } = "";
    public string DefaultSmallImageText { get; set; } = "";
    public string DefaultDetails { get; set; } = "";
    public string DefaultState { get; set; } = "";
    public TimestampMode DefaultTimestamps { get; set; } = TimestampMode.SinceStart;

    // ---- Optional auto-update defaults so well-known apps work out of the box ----
    public AutoUpdateConfig? DefaultAutoUpdate { get; set; }

    // ---- Pre-made scenarios within this app (e.g. "Survival" / "Creative" for Minecraft) ----
    public List<AppScenario> Scenarios { get; set; } = new();

    public string? Notes { get; set; }
    public string? DocsUrl { get; set; }
}

public sealed class AppScenario
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Details { get; set; } = "";
    public string State { get; set; } = "";
    public string LargeImageKey { get; set; } = "";
    public string SmallImageKey { get; set; } = "";
}

public sealed class DetectedApp
{
    public string ProcessName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public int Pid { get; set; }
    public string? ExecutablePath { get; set; }
    public KnownApp? Match { get; set; }
}
