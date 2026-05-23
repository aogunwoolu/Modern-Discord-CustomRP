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
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "thorium", "librewolf", "zen",
    };

    /// <summary>
    /// Known AutomationIds for the address bar in Gecko-based browsers.
    /// Searched directly (no ControlType filter) as a fallback when the
    /// Edit-control walk fails.
    /// </summary>
    private static readonly string[] GeckoUrlBarIds =
    {
        "urlbar-input",  // Firefox 70+ and Zen Browser
        "urlbar",        // older Gecko / some Zen builds
    };

    public static bool IsBrowser(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        return KnownBrowsers.Contains(System.IO.Path.GetFileNameWithoutExtension(processName));
    }

    /// <summary>Diagnostic — how many Edit controls the last UIA walk surfaced.</summary>
    public int LastEditCount { get; private set; }

    /// <summary>
    /// Diagnostic — semicolon-separated details for every Edit found during the
    /// last walk: aid / name / value snippet. Empty when needUrl=false.
    /// </summary>
    public string LastEditDiag { get; private set; } = "";

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

            // --- Pass 1: walk the tree looking for Edit controls ---
            string? addressy = null;
            string? urlLooking = null;
            var editCount = 0;
            var diagLines = new List<string>();
            CollectEditCandidates(window.AsWindow(), depth: 0, budget: 600,
                ref addressy, ref urlLooking, ref editCount, diagLines);
            LastEditCount = editCount;
            LastEditDiag  = diagLines.Count > 0 ? string.Join(" | ", diagLines) : "";

            var picked = addressy ?? urlLooking;
            if (picked is not null) return Normalize(picked);

            // --- Pass 2: direct AutomationId lookup for Gecko browsers ---
            // Zen/Firefox may not expose the URL bar as ControlType.Edit while
            // it is unfocused, but FindFirstDescendant searches all control types.
            foreach (var id in GeckoUrlBarIds)
            {
                try
                {
                    var el = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
                    if (el is null) continue;
                    var val = TryGetValue(el);
                    if (!string.IsNullOrWhiteSpace(val) && LooksLikeUrl(val))
                        return Normalize(val);
                }
                catch { }
            }

            // --- Pass 3: Gecko session-file fallback ---
            // Zen Browser (and Firefox/LibreWolf) write the active-tab URL to a
            // mozlz4 session file every ~15 s. This is the only reliable source
            // when UIA cross-process IPC is blocked by Zen's process isolation.
            {
                var sessionUrl = GeckoSessionReader.TryGetActiveTabUrl(process.ProcessName);
                if (sessionUrl is not null) return sessionUrl;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void CollectEditCandidates(
        AutomationElement element, int depth, int budget,
        ref string? addressy, ref string? urlLooking, ref int editCount,
        List<string> diagLines)
    {
        if (element is null || budget <= 0 || depth > 16 || addressy is not null) return;

        try
        {
            if (element.ControlType == ControlType.Edit)
            {
                editCount++;
                var automId = element.AutomationId ?? "";
                var name    = element.Name ?? "";
                var hint    = automId + " " + name;
                var isAddressy = hint.IndexOf("address",  StringComparison.OrdinalIgnoreCase) >= 0
                              || hint.IndexOf("url",      StringComparison.OrdinalIgnoreCase) >= 0
                              || hint.IndexOf("omnibox",  StringComparison.OrdinalIgnoreCase) >= 0
                              || hint.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0
                              || hint.IndexOf("search",   StringComparison.OrdinalIgnoreCase) >= 0;
                var value = TryGetValue(element);

                var valSnip = value is null ? "<null>"
                    : value.Length > 50 ? value[..50] + "…" : value;
                diagLines.Add($"aid='{automId}' name='{name}' val='{valSnip}' isAddr={isAddressy}");

                if (!string.IsNullOrWhiteSpace(value) && LooksLikeUrl(value))
                {
                    if (isAddressy) { addressy = value; return; }
                    if (urlLooking is null) urlLooking = value;
                }
            }
        }
        catch { }

        AutomationElement[] children;
        try { children = element.FindAllChildren(); }
        catch { return; }

        var remaining = budget - children.Length;
        foreach (var child in children)
        {
            CollectEditCandidates(child, depth + 1, remaining,
                ref addressy, ref urlLooking, ref editCount, diagLines);
            if (addressy is not null) return;
        }
    }

    private static bool LooksLikeUrl(string raw)
    {
        var s = raw.Trim();
        if (s.Length < 4) return false;
        if (s.Contains("://")) return true;
        if (s.Contains(' ')) return false;
        var firstDot = s.IndexOf('.');
        return firstDot > 0 && firstDot < s.Length - 1;
    }

    private static string? TryGetValue(AutomationElement el)
    {
        // ValuePattern — Chromium browsers always have the URL here.
        try
        {
            var vp = el.Patterns.Value.PatternOrDefault;
            if (vp is not null)
            {
                var val = vp.Value.ValueOrDefault;
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        catch { }

        // TextPattern — some Gecko builds expose the URL via TextPattern
        // when ValuePattern returns empty (e.g. Zen Browser).
        try
        {
            var tp = el.Patterns.Text.PatternOrDefault;
            if (tp is not null)
            {
                var text = tp.DocumentRange.GetText(512);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch { }

        // Name property — Gecko sets this to the current URL when the bar
        // is not focused and ValuePattern / TextPattern return empty.
        return el.Name;
    }

    private static string Normalize(string raw)
    {
        var url = raw.Trim();
        if (!url.Contains("://") && (url.Contains('.') || url.StartsWith("localhost")))
            url = "https://" + url;
        return url;
    }

    public void Dispose() => _automation.Dispose();
}
