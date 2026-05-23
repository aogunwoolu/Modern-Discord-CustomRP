using System;
using System.Collections.Concurrent;
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
    private static readonly HttpClient Http = MakeHttpClient();

    private static HttpClient MakeHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        return client;
    }

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
    /// Returns a Discord-compatible favicon URL for the given page URL.
    /// Uses wsrv.nl as the public image proxy: it fetches the DuckDuckGo
    /// favicon (which covers most sites), converts ICO→PNG on the fly, and
    /// serves it from a CDN that Discord’s media proxy can access.
    /// If a Katsau API key is configured the result is upgraded in the
    /// background to the highest-quality icon and cached for future calls.
    /// </summary>
    public string? GetDiscordCompatibleUrl(string url)
    {
        if (!TryGetHost(url, out var host)) return null;

        // Katsau upgrade: if an API key is set and we have a cached result, use it.
        if (_hostToDiscordUrl.TryGetValue(host, out var cached) && cached.Length > 0)
            return cached;

        // Kick off the Katsau background upgrade if a key is configured and
        // this host hasn’t been attempted yet.
        var apiKey = _settings.Current.KatsauApiKey?.Trim();
        if (!string.IsNullOrEmpty(apiKey) && !_hostToDiscordUrl.ContainsKey(host))
            _ = Task.Run(() => ResolveKatsauAsync(host, url, apiKey));

        // Immediate return via wsrv.nl: fetches DuckDuckGo’s favicon for the
        // host, converts ICO→PNG, and serves it from a CDN Discord can reach.
        return WsrvFaviconUrl(host);
    }

    private static string WsrvFaviconUrl(string host)
    {
        var ddg = Uri.EscapeDataString($"icons.duckduckgo.com/ip3/{host}.ico");
        return $"https://wsrv.nl/?url={ddg}&output=png&w=64&h=64";
    }

    private async Task ResolveKatsauAsync(string host, string originalUrl, string apiKey)
    {
        // Mark as attempted immediately to prevent duplicate background tasks.
        _hostToDiscordUrl[host] = "";

        var katsauUrl = await TryKatsauAsync(originalUrl, apiKey).ConfigureAwait(false);
        if (katsauUrl is not null)
            _hostToDiscordUrl[host] = katsauUrl;
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
