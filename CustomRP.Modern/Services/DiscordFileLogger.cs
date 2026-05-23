using DiscordRPC.Logging;
using System;
using System.IO;

namespace CustomRP.Modern.Services;

/// <summary>
/// Bridges Lachee's <see cref="ILogger"/> to a rolling text file under
/// %LOCALAPPDATA%/CustomRP.Modern/discord-rpc.log. Without this the library
/// fails silently — and Discord's presence pipeline drops bad payloads
/// without surfacing anything to the host app.
/// </summary>
public sealed class DiscordFileLogger : ILogger
{
    private readonly string _path;
    private readonly object _gate = new();

    public LogLevel Level { get; set; } = LogLevel.Info;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CustomRP.Modern", "discord-rpc.log");

    public DiscordFileLogger(string? path = null)
    {
        _path = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Append("--- session start ---");
    }

    public void Trace(string message, params object[] args) => Write(LogLevel.Trace, message, args);
    public void Info(string message, params object[] args) => Write(LogLevel.Info, message, args);
    public void Warning(string message, params object[] args) => Write(LogLevel.Warning, message, args);
    public void Error(string message, params object[] args) => Write(LogLevel.Error, message, args);

    private void Write(LogLevel level, string message, params object[] args)
    {
        if (level < Level) return;
        try
        {
            var formatted = args.Length == 0 ? message : string.Format(message, args);
            Append($"[{DateTime.Now:HH:mm:ss.fff}] {level,-7} {formatted}");
        }
        catch
        {
            // Logger must never throw or we'll cascade through the RPC worker.
        }
    }

    private void Append(string line)
    {
        lock (_gate)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); }
            catch { /* swallow */ }
        }
    }
}
