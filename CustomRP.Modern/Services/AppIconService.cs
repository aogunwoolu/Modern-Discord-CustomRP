using CustomRP.Modern.Models;
using System;
using System.Collections.Generic;

namespace CustomRP.Modern.Services;

/// <summary>
/// Resolves a PNG icon URL for a <see cref="KnownApp"/> using public icon CDNs.
/// Priority: explicit <c>iconUrl</c> field in known-apps.json → Dashboard Icons CDN slug.
/// Failed loads degrade silently (the UrlImage control caches nulls on error).
/// </summary>
public static class AppIconService
{
    private const string DashBase =
        "https://raw.githubusercontent.com/walkxcode/dashboard-icons/main/png/";

    private static readonly IReadOnlyDictionary<string, string> Slugs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Music ──────────────────────────────────────────────────
            ["spotify"]           = "spotify",
            ["ytmusic"]           = "youtube-music",
            ["apple-music"]       = "apple-music",
            ["tidal"]             = "tidal",
            ["deezer"]            = "deezer",

            // ── Video / Streaming ───────────────────────────────────────
            ["netflix"]           = "netflix",
            ["youtube"]           = "youtube",
            ["twitch"]            = "twitch",
            ["crunchyroll"]       = "crunchyroll",
            ["plex"]              = "plex",
            ["jellyfin"]          = "jellyfin",

            // ── Development ─────────────────────────────────────────────
            ["vscode"]            = "visual-studio-code",
            ["vscode-insiders"]   = "visual-studio-code-insiders",
            ["cursor"]            = "cursor",
            ["zed"]               = "zed",
            ["neovim"]            = "neovim",
            ["visualstudio"]      = "visual-studio",
            ["rider"]             = "jetbrains-rider",
            ["intellij"]          = "intellij-idea",
            ["pycharm"]           = "pycharm",
            ["webstorm"]          = "webstorm",
            ["clion"]             = "clion",
            ["goland"]            = "goland",
            ["phpstorm"]          = "phpstorm",
            ["android-studio"]    = "android-studio",
            ["github-desktop"]    = "github",
            ["postman"]           = "postman",
            ["docker-desktop"]    = "docker",
            ["terminal"]          = "windows-terminal",

            // ── Creative ────────────────────────────────────────────────
            ["photoshop"]         = "adobe-photoshop",
            ["illustrator"]       = "adobe-illustrator",
            ["premiere"]          = "adobe-premiere-pro",
            ["ae"]                = "adobe-after-effects",
            ["lightroom"]         = "adobe-lightroom",
            ["figma"]             = "figma",
            ["blender"]           = "blender",
            ["davinci"]           = "davinci-resolve",
            ["ableton"]           = "ableton-live",
            ["obs"]               = "obs-studio",
            ["streamlabs"]        = "streamlabs",

            // ── Productivity ─────────────────────────────────────────────
            ["notion"]            = "notion",
            ["obsidian"]          = "obsidian",
            ["logseq"]            = "logseq",
            ["word"]              = "microsoft-word",
            ["excel"]             = "microsoft-excel",
            ["powerpoint"]        = "microsoft-powerpoint",
            ["onenote"]           = "microsoft-onenote",
            ["outlook"]           = "microsoft-outlook",

            // ── Browsers ─────────────────────────────────────────────────
            ["chrome"]            = "google-chrome",
            ["edge"]              = "microsoft-edge",
            ["firefox"]           = "firefox",
            ["brave"]             = "brave",
            ["opera"]             = "opera",
            ["vivaldi"]           = "vivaldi",
            ["arc"]               = "arc",

            // ── Communication ────────────────────────────────────────────
            ["discord"]           = "discord",
            ["slack"]             = "slack",
            ["teams"]             = "microsoft-teams",
            ["zoom"]              = "zoom",
            ["telegram"]          = "telegram",

            // ── Game Launchers ───────────────────────────────────────────
            ["steam"]             = "steam",
            ["epic"]              = "epic-games",
            ["battlenet"]         = "battle-net",
            ["gog"]               = "gog-galaxy",
            ["ubisoft-connect"]   = "ubisoft-connect",
            ["ea-desktop"]        = "ea",
        };

    /// <summary>
    /// Returns a resolvable PNG URL for the app icon.
    /// Priority: explicit <c>iconUrl</c> field → known slug mapping → app ID used as slug
    /// directly (UrlImage and Discord degrade silently on 404s).
    /// </summary>
    public static string? Resolve(KnownApp app)
    {
        if (!string.IsNullOrWhiteSpace(app.IconUrl))
            return app.IconUrl;

        if (Slugs.TryGetValue(app.Id, out var slug))
            return DashBase + slug + ".png";

        // Last resort: try the app ID as a Dashboard Icons slug.
        // Many IDs match the CDN naming exactly; 404s are silently dropped.
        return string.IsNullOrWhiteSpace(app.Id) ? null : DashBase + app.Id + ".png";
    }
}
