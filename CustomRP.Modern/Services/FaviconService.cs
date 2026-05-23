using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CustomRP.Modern.Services;

/// <summary>
/// Resolves a favicon for a given URL and caches the result on disk under
/// %LOCALAPPDATA%/CustomRP.Modern/favicons. Returns a file:// URI suitable
/// for setting as Discord's small image asset URL.
/// </summary>
public sealed class FaviconService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private readonly SettingsService _settings;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, string> _hostToPath = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Verified per-host Discord-compatible favicon URL (populated by background resolver).</summary>
    private readonly ConcurrentDictionary<string, string> _hostToDiscordUrl = new(StringComparer.OrdinalIgnoreCase);

    public FaviconService(SettingsService settings)
    {
        _settings = settings;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomRP.Modern", "favicons");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Returns the best available Discord-compatible favicon URL for the given site.
    /// Tier priority:
    ///   0. Katsau API (if key configured) — returns the site's actual favicon URL
    ///   1. Google S2 (128 px PNG — highest quality; Discord proxy fetches the full URL
    ///      including query string, so ?domain=…&sz= is preserved at fetch time)
    ///   2. DuckDuckGo IP3 (.ico fallback)
    ///   3. Direct /favicon.ico on the origin host
    /// The first call returns Google S2 immediately; background verification upgrades
    /// to Katsau (if available) and caches the best result for subsequent calls.
    /// </summary>
    public string? GetDiscordCompatibleUrl(string url)
    {
        if (!TryGetHost(url, out var host)) return null;

        // Return the verified winner from a previous resolution cycle.
        if (_hostToDiscordUrl.TryGetValue(host, out var cached)) return cached;

        // Kick off background verification for future calls (fire-and-forget).
        _ = Task.Run(() => ResolveDiscordUrlAsync(host, url));

        // Optimistic immediate return — Google S2 returns PNG which Discord handles well.
        return GoogleFaviconUrl(host);
    }

    private static string GoogleFaviconUrl(string host) =>
        $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=128";

    private static string DuckDuckGoFaviconUrl(string host) =>
        $"https://icons.duckduckgo.com/ip3/{host}.ico";

    private async Task ResolveDiscordUrlAsync(string host, string originalUrl)
    {
        // Tier 0 (optional): Katsau API — parses the actual page HTML for the
        // highest-quality favicon. Requires a user-supplied API key.
        var apiKey = _settings.Current.KatsauApiKey?.Trim();
        if (!string.IsNullOrEmpty(apiKey))
        {
            var katsauUrl = await TryKatsauAsync(originalUrl, apiKey).ConfigureAwait(false);
            if (katsauUrl is not null)
            {
                _hostToDiscordUrl[host] = katsauUrl;
                return;
            }
        }

        // Tiers 1–3: verify in descending quality order.
        var tiers = new List<string>
        {
            GoogleFaviconUrl(host),
            DuckDuckGoFaviconUrl(host),
            $"https://{host}/favicon.ico",
        };

        foreach (var candidate in tiers)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, candidate);
                using var resp = await Http.SendAsync(
                    req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (resp.IsSuccessStatusCode &&
                    ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    _hostToDiscordUrl[host] = candidate;
                    return;
                }
            }
            catch { }
        }

        // All tiers failed — fall back to DuckDuckGo silently.
        _hostToDiscordUrl[host] = $"https://icons.duckduckgo.com/ip3/{host}.ico";
    }

    /// <summary>
    /// Calls the Katsau favicon API (https://api.katsau.com/v1/favicon) with
    /// the user's API key and returns the best favicon URL, or null on failure.
    /// </summary>
    private static async Task<string?> TryKatsauAsync(string pageUrl, string apiKey)
    {
        try
        {
            var endpoint = $"https://api.katsau.com/v1/favicon?url={Uri.EscapeDataString(pageUrl)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("favicon", out var faviconProp))
            {
                var url = faviconProp.GetString();
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Best-effort local cache of the favicon for preview purposes. Returns a
    /// file path or null if it could not be fetched. Safe to call repeatedly.
    /// </summary>
    public async Task<string?> GetCachedFavicon(string url)
    {
        if (!TryGetHost(url, out var host)) return null;
        if (_hostToPath.TryGetValue(host, out var cached) && File.Exists(cached))
            return cached;

        var path = Path.Combine(_cacheDir, host + ".png");
        if (File.Exists(path))
        {
            _hostToPath[host] = path;
            return path;
        }

        try
        {
            var proxy = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=64";
            using var stream = await Http.GetStreamAsync(proxy);
            await using var file = File.Create(path);
            await stream.CopyToAsync(file);
            _hostToPath[host] = path;
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetHost(string url, out string host)
    {
        host = "";
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.Contains("://")) url = "https://" + url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               !string.IsNullOrEmpty(host = uri.Host);
    }
}
