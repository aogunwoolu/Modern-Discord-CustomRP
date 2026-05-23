using Avalonia;
using System;
using System.IO;

namespace CustomRP.Modern;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
