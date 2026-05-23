using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CustomRP.Modern.Services;

/// <summary>
/// Reads the URL currently displayed in a Chromium/Gecko browser's address bar
/// via the Windows UI Automation tree. Pure read; no input is synthesized.
/// </summary>
public sealed class BrowserUrlReader : IDisposable
{
    private readonly UIA3Automation _automation = new();

    /// <summary>
    /// Process names this reader recognises as browsers.
    /// </summary>
    public static readonly HashSet<string> KnownBrowsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "thorium", "librewolf",
    };

    public static bool IsBrowser(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        return KnownBrowsers.Contains(System.IO.Path.GetFileNameWithoutExtension(processName));
    }

    /// <summary>
    /// Attempts to read the current page URL from the supplied process's main window.
    /// Returns null if it cannot find an address bar or the process has no main window.
    /// </summary>
    public string? TryReadUrl(Process process)
    {
        if (process.MainWindowHandle == IntPtr.Zero) return null;

        try
        {
            var window = _automation.FromHandle(process.MainWindowHandle);
            if (window is null) return null;

            // The address bar is an Edit control. Walk a bounded subtree —
            // bail after a few hundred elements to keep tick latency low.
            return FindAddressBarValue(window.AsWindow(), depth: 0, budget: 600);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindAddressBarValue(AutomationElement element, int depth, int budget)
    {
        if (element is null || budget <= 0 || depth > 12) return null;

        try
        {
            if (element.ControlType == ControlType.Edit)
            {
                // Edit controls inside a browser are typically the address bar.
                // We prefer ones whose AutomationId or Name suggests "address",
                // "url", or "omnibox" — but fall back to the first non-empty edit.
                var hint = (element.AutomationId ?? "") + " " + (element.Name ?? "");
                var isAddressy = hint.IndexOf("address", StringComparison.OrdinalIgnoreCase) >= 0
                              || hint.IndexOf("url", StringComparison.OrdinalIgnoreCase) >= 0
                              || hint.IndexOf("omnibox", StringComparison.OrdinalIgnoreCase) >= 0
                              || hint.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0;
                var value = TryGetValue(element);
                if (isAddressy && !string.IsNullOrWhiteSpace(value))
                    return Normalize(value);
            }
        }
        catch { /* element accessor threw — skip */ }

        AutomationElement[] children;
        try { children = element.FindAllChildren(); }
        catch { return null; }

        var remaining = budget - children.Length;
        foreach (var child in children)
        {
            var result = FindAddressBarValue(child, depth + 1, remaining);
            if (result is not null) return result;
        }
        return null;
    }

    private static string? TryGetValue(AutomationElement el)
    {
        try
        {
            var vp = el.Patterns.Value.PatternOrDefault;
            if (vp is not null) return vp.Value.ValueOrDefault;
        }
        catch { /* not supported */ }
        return el.Name;
    }

    private static string Normalize(string raw)
    {
        var url = raw.Trim();
        // Browsers often elide the scheme — re-add it so favicon/template
        // consumers can parse the URL.
        if (!url.Contains("://") && (url.Contains('.') || url.StartsWith("localhost")))
            url = "https://" + url;
        return url;
    }

    public void Dispose() => _automation.Dispose();
}
