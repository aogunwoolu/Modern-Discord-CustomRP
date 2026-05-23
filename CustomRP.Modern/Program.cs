using Avalonia;
using CustomRP.Modern.Services;
using System;
using System.IO;

namespace CustomRP.Modern;

internal static class Program
{
    public const string WorkerArg = "--worker";

    [STAThread]
    public static int Main(string[] args)
    {
        // Worker child process: no UI, just shuttle Discord RPC over stdin/stdout.
        // Each active presence runs in its own worker so Discord can't dedupe
        // them by PID.
        if (args.Length > 0 && args[0] == WorkerArg)
            return WorkerEntry.Run();

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomRP.Modern", "startup.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] UNHANDLED: {ex.ExceptionObject}\n");

        try
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] starting\n");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] exited cleanly\n");
            return 0;
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] CRASH: {ex}\n");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
