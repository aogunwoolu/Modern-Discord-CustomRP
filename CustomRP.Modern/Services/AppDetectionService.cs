using CustomRP.Modern.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CustomRP.Modern.Services;

/// <summary>
/// Scans currently running processes and surfaces them as <see cref="DetectedApp"/>s,
/// annotating each with a matching <see cref="KnownApp"/> when available.
/// </summary>
public sealed class AppDetectionService
{
    private readonly KnownAppsRegistry _registry;

    private static readonly HashSet<string> NoiseProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "csrss", "smss", "wininit", "winlogon",
        "lsass", "services", "fontdrvhost", "dwm", "taskhostw", "runtimebroker",
        "sihost", "ctfmon", "searchhost", "searchapp", "shellexperiencehost",
        "startmenuexperiencehost", "applicationframehost", "textinputhost",
        "system", "registry", "memcompression", "secureservicehost", "winston",
        "conhost", "audiodg", "spoolsv", "dllhost", "mscorsvw",
    };

    public AppDetectionService(KnownAppsRegistry registry)
    {
        _registry = registry;
    }

    public Task<IReadOnlyList<DetectedApp>> ScanAsync() => Task.Run(() => Scan());

    public IReadOnlyList<DetectedApp> Scan()
    {
        var results = new Dictionary<string, DetectedApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                if (NoiseProcesses.Contains(name)) continue;
                if (results.ContainsKey(name)) continue;
                if (string.IsNullOrEmpty(process.MainWindowTitle) &&
                    _registry.MatchProcess(name) is null)
                    continue;

                string? path = null;
                try { path = process.MainModule?.FileName; } catch { /* access denied */ }

                results[name] = new DetectedApp
                {
                    ProcessName = name,
                    WindowTitle = process.MainWindowTitle ?? "",
                    Pid = process.Id,
                    ExecutablePath = path,
                    Match = _registry.MatchProcess(name),
                };
            }
            catch
            {
                // Some processes will throw on access — ignore them.
            }
            finally
            {
                process.Dispose();
            }
        }

        return results.Values
            .OrderByDescending(d => d.Match != null)
            .ThenBy(d => d.ProcessName)
            .ToList();
    }
}
