using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
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

    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, string> _hostToPath = new(StringComparer.OrdinalIgnoreCase);

    public FaviconService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomRP.Modern", "favicons");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Returns a URL Discord can render for the favicon of the given site.
    /// Uses DuckDuckGo's icon API — a clean path URL with no query string,
    /// which Discord's mp:external proxy handles correctly.
    /// </summary>
    public string? GetDiscordCompatibleUrl(string url)
    {
        if (!TryGetHost(url, out var host)) return null;
        return $"https://icons.duckduckgo.com/ip3/{host}.ico";
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
