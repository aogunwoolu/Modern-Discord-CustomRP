using System;
using System.IO;
using System.Text.Json;

namespace CustomRP.Modern.Services;

/// <summary>
/// Entry point for the <c>--worker</c> child process. The parent spawns one of
/// these per active rich presence so each presence has a distinct PID — Discord
/// otherwise collapses every activity coming from the same process into one
/// visible slot.
/// </summary>
internal static class WorkerEntry
{
    public static int Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var stdin = Console.In;
        string? firstLine;
        try { firstLine = stdin.ReadLine(); }
        catch { return 2; }

        if (string.IsNullOrEmpty(firstLine)) return 3;
        // Strip any UTF-8 BOM the parent's stream encoding may have prepended.
        if (firstLine[0] == '﻿') firstLine = firstLine.Substring(1);

        WorkerCommand? init;
        try { init = JsonSerializer.Deserialize<WorkerCommand>(firstLine, WorkerJson.Options); }
        catch { return 5; }

        if (init is null || init.Cmd != "init" || init.Preset is null
            || string.IsNullOrWhiteSpace(init.Preset.ClientId))
            return 6;

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomRP.Modern", "workers",
            $"{Sanitize(init.Preset.ClientId)}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var logger = new DiscordFileLogger(logPath);
        var emitGate = new object();

        void Emit(WorkerEvent e)
        {
            var line = JsonSerializer.Serialize(e, WorkerJson.Options);
            lock (emitGate)
            {
                try { Console.Out.WriteLine(line); Console.Out.Flush(); }
                catch { /* parent gone */ }
            }
        }

        using var session = new DiscordRpcSession(
            init.Preset.ClientId,
            init.Pipe,
            init.Preset,
            Emit,
            logger);

        session.Connect();

        // Pump commands until parent closes stdin.
        while (true)
        {
            string? line;
            try { line = stdin.ReadLine(); }
            catch { break; }
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            WorkerCommand? cmd;
            try { cmd = JsonSerializer.Deserialize<WorkerCommand>(line, WorkerJson.Options); }
            catch { continue; }
            if (cmd is null) continue;

            switch (cmd.Cmd)
            {
                case "update":
                    if (cmd.Preset is not null) session.UpdatePresence(cmd.Preset);
                    break;
                case "stop":
                    session.Disconnect();
                    return 0;
            }
        }

        session.Disconnect();
        return 0;
    }

    private static string Sanitize(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        for (int i = 0; i < s.Length; i++)
            buf[i] = char.IsLetterOrDigit(s[i]) ? s[i] : '_';
        return new string(buf);
    }
}
