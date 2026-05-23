using CustomRP.Modern.Models;
using System.Text.Json;

namespace CustomRP.Modern.Services;

/// <summary>
/// Line-delimited JSON protocol between the parent UI process and a worker
/// child process. Each worker owns one Discord RPC connection; running each
/// presence in its own process gives every presence a distinct PID so Discord
/// stops deduping them under a single application activity slot.
/// </summary>
public sealed class WorkerCommand
{
    public string Cmd { get; set; } = "";
    public int Pipe { get; set; } = -1;
    public string? DisplayName { get; set; }
    public Preset? Preset { get; set; }
}

public sealed class WorkerEvent
{
    public string Event { get; set; } = "";
    public string? State { get; set; }
    public string? Message { get; set; }
    public string? Username { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Summary { get; set; }
}

internal static class WorkerJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonDefaults.Options)
    {
        WriteIndented = false,
    };
}
