using CustomRP.Modern.Models;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CustomRP.Modern.Services;

/// <summary>
/// Polls a target process at a configurable interval, capturing its window
/// title and (for browsers) the current URL via UI Automation. Emits a
/// <see cref="LiveSnapshot"/> on every tick that produced a change.
/// </summary>
public sealed class AutoUpdateService : IDisposable
{
    private readonly BrowserUrlReader _urlReader = new();
    private readonly FaviconService _favicons;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AutoUpdateConfig _config = new();

    public LiveSnapshot? Current { get; private set; }
    public event EventHandler<LiveSnapshot>? Updated;

    public AutoUpdateService(FaviconService favicons)
    {
        _favicons = favicons;
    }

    public void Configure(AutoUpdateConfig config)
    {
        _config = config;
        if (config.Enabled && config.Strategy != AutoUpdateStrategy.Off &&
            !string.IsNullOrWhiteSpace(config.ProcessName))
            Start();
        else
            Stop();
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _loop = null;
    }

    private async Task Loop(CancellationToken ct)
    {
        // Snapshot the config locally — Configure() may swap _config out
        // mid-flight, but we want stable behaviour within one iteration.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var config = _config;
                var snapshot = await CaptureAsync(config);
                if (snapshot is not null && IsDifferent(Current, snapshot))
                {
                    Current = snapshot;
                    Updated?.Invoke(this, snapshot);
                }
            }
            catch
            {
                // Never let a transient UIA / process failure kill the loop.
            }

            var interval = Math.Max(1, _config.IntervalSeconds);
            try { await Task.Delay(TimeSpan.FromSeconds(interval), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<LiveSnapshot?> CaptureAsync(AutoUpdateConfig config)
    {
        var process = FindProcess(config.ProcessName);
        if (process is null)
        {
            return new LiveSnapshot
            {
                ProcessName = config.ProcessName,
                ProcessFound = false,
            };
        }

        try
        {
            var title = process.MainWindowTitle ?? "";
            string? url = null;
            string? favicon = null;

            if (config.Strategy == AutoUpdateStrategy.BrowserUrl &&
                BrowserUrlReader.IsBrowser(process.ProcessName))
            {
                url = _urlReader.TryReadUrl(process);
                if (config.UseFaviconAsSmallImage && !string.IsNullOrWhiteSpace(url))
                    favicon = _favicons.GetDiscordCompatibleUrl(url);
            }

            return new LiveSnapshot
            {
                ProcessName = process.ProcessName,
                WindowTitle = title,
                Url = url,
                FaviconUrl = favicon,
                ProcessFound = true,
            };
        }
        finally
        {
            process.Dispose();
        }
    }

    private static Process? FindProcess(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        // Only strip a trailing .exe — Path.GetFileNameWithoutExtension would
        // incorrectly strip ".net" from "battle.net" as if it were an extension.
        var bare = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4] : name;
        var all = Process.GetProcessesByName(bare);
        // Prefer processes with a visible window; fall back to the largest
        // process so background/tray apps (Spotify, music players, etc.) work.
        return all.Where(p => p.MainWindowHandle != IntPtr.Zero)
                   .OrderByDescending(p => p.WorkingSet64)
                   .FirstOrDefault()
               ?? all.OrderByDescending(p => p.WorkingSet64).FirstOrDefault();
    }

    private static bool IsDifferent(LiveSnapshot? a, LiveSnapshot b) =>
        a is null
        || a.WindowTitle != b.WindowTitle
        || a.Url != b.Url
        || a.ProcessFound != b.ProcessFound;

    public void Dispose()
    {
        Stop();
        _urlReader.Dispose();
    }
}

/// <summary>
/// Applies a preset's auto-update templates to a live snapshot.
/// Supports both {var} and {{var}} syntax.
/// </summary>
public static class TemplateRenderer
{
    /// <summary>All recognised variable names, in resolution order.</summary>
    private static readonly string[] VarNames =
        { "title", "url", "host", "path", "scheme", "query", "port", "process" };

    public static string Render(string template, LiveSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(template)) return "";

        Uri.TryCreate(snapshot.Url, UriKind.Absolute, out var uri);

        string Value(string key) => key switch
        {
            "title"   => snapshot.WindowTitle,
            "url"     => snapshot.Url ?? "",
            "host"    => uri?.Host ?? "",
            "path"    => uri?.AbsolutePath ?? "",
            "scheme"  => uri?.Scheme ?? "",
            "query"   => uri?.Query.TrimStart('?') ?? "",
            "port"    => uri is { IsDefaultPort: false } ? uri.Port.ToString() : "",
            "process" => snapshot.ProcessName,
            _         => "",
        };

        var result = template;
        foreach (var name in VarNames)
        {
            var val = Value(name);
            // Replace double-brace first (longer token) then single-brace.
            result = result.Replace($"{{{{{name}}}}}", val, StringComparison.OrdinalIgnoreCase);
            result = result.Replace($"{{{name}}}", val, StringComparison.OrdinalIgnoreCase);
        }

        return result.Length > 128 ? result[..128] : result;
    }
}
