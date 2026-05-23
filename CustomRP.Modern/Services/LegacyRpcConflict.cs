using System.Diagnostics;
using System.Linq;

namespace CustomRP.Modern.Services;

/// <summary>
/// Discord allows one IPC client per application ID. The classic CustomRP.exe
/// must be closed or disconnected before Modern can own the same Client ID.
/// </summary>
public static class LegacyRpcConflict
{
    public static bool IsLegacyCustomRpRunning() =>
        Process.GetProcessesByName("CustomRP").Any(p => !p.HasExited);

    public static string WarningMessage =>
        "Classic CustomRP (CustomRP.exe) is still running. Quit or disconnect it first — " +
        "only one app can control the same Client ID on Discord.";
}
