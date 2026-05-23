using CustomRP.Modern.Models;
using DiscordRPC.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CustomRP.Modern.Services;

public enum RpcConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

public sealed class RpcStateChangedEventArgs : EventArgs
{
    public RpcConnectionState State { get; }
    public string? Message { get; }
    public string? Username { get; }
    public string? AvatarUrl { get; }

    public RpcStateChangedEventArgs(RpcConnectionState state, string? message = null,
        string? username = null, string? avatarUrl = null)
    {
        State = state;
        Message = message;
        Username = username;
        AvatarUrl = avatarUrl;
    }
}

/// <summary>
/// Parent-side handle for one Discord RPC connection. The actual Discord
/// client lives inside a child worker process so each active presence has its
/// own PID — without that, Discord deduplicates every activity coming from
/// the same process into a single visible slot regardless of Client ID.
/// </summary>
public sealed class RpcConnection : IDisposable
{
    private readonly object _gate = new();
    private Process? _proc;
    private CancellationTokenSource? _readerCts;
    private RpcConnectionState _state = RpcConnectionState.Disconnected;

    public string ClientId { get; }
    public string DisplayName { get; set; }
    public Preset Preset { get; set; }
    public int Pipe { get; set; } = -1;

    public string? Username { get; private set; }
    public string? AvatarUrl { get; private set; }
    public DateTime? LastSentAt { get; private set; }
    public string? LastSentSummary { get; private set; }
    public string? LastError { get; private set; }
    public string? StatusMessage { get; private set; }

    public RpcConnectionState State => _state;

    public event EventHandler<RpcStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? PresenceSent;
    public event EventHandler<string>? PresenceFailed;

    // Ctor kept compatible with the in-process version — the logger arg is
    // ignored here because each worker writes its own log file.
    public RpcConnection(string clientId, string displayName, Preset preset, ILogger? _ = null)
    {
        ClientId = clientId;
        DisplayName = displayName;
        Preset = preset;
    }

    public void Connect()
    {
        lock (_gate)
        {
            StopWorker();

            var exe = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "CustomRP.Modern.exe");

            // BOM-less UTF-8 — Encoding.UTF8 emits a BOM that fails JSON parsing
            // on the worker side.
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = Program.WorkerArg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = utf8,
                StandardOutputEncoding = utf8,
            };

            SetState(RpcConnectionState.Connecting, "Spawning worker…");

            Process proc;
            try { proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null"); }
            catch (Exception ex)
            {
                SetState(RpcConnectionState.Error, $"Could not spawn worker: {ex.Message}");
                return;
            }

            _proc = proc;

            // Ship the init command — pipe, displayName, full preset including
            // resolved ClientId — and let the worker handle Discord from here.
            Preset.ClientId = ClientId;
            var init = new WorkerCommand
            {
                Cmd = "init",
                Pipe = Pipe,
                DisplayName = DisplayName,
                Preset = Preset,
            };
            try
            {
                proc.StandardInput.WriteLine(JsonSerializer.Serialize(init, WorkerJson.Options));
                proc.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                SetState(RpcConnectionState.Error, $"Worker handshake failed: {ex.Message}");
                StopWorker();
                return;
            }

            _readerCts = new CancellationTokenSource();
            var token = _readerCts.Token;
            _ = Task.Run(() => PumpEvents(proc, token), token);

            // Drain stderr so the child doesn't block on a full pipe buffer.
            _ = Task.Run(async () =>
            {
                try { await proc.StandardError.ReadToEndAsync().ConfigureAwait(false); }
                catch { }
            });
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            StopWorker();
            if (_state != RpcConnectionState.Disconnected)
                SetState(RpcConnectionState.Disconnected, "Disconnected.");
        }
    }

    public void UpdatePresence(Preset preset)
    {
        lock (_gate)
        {
            Preset = preset;
            if (_proc is null || _proc.HasExited)
            {
                const string msg = "Not connected to Discord.";
                LastError = msg;
                PresenceFailed?.Invoke(this, msg);
                return;
            }

            try
            {
                preset.ClientId = ClientId;
                var cmd = new WorkerCommand { Cmd = "update", Preset = preset };
                _proc.StandardInput.WriteLine(JsonSerializer.Serialize(cmd, WorkerJson.Options));
                _proc.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                PresenceFailed?.Invoke(this, ex.Message);
            }
        }
    }

    public void Dispose() => Disconnect();

    private void PumpEvents(Process proc, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line;
                try { line = proc.StandardOutput.ReadLine(); }
                catch { break; }
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                WorkerEvent? evt;
                try { evt = JsonSerializer.Deserialize<WorkerEvent>(line, WorkerJson.Options); }
                catch { continue; }
                if (evt is null) continue;

                HandleEvent(evt);
            }
        }
        catch (Exception ex)
        {
            // Worker pipe died unexpectedly — surface as error so UI updates.
            SetState(RpcConnectionState.Error, $"Worker pipe lost: {ex.Message}");
        }
        finally
        {
            // If the worker exited on its own, reflect Disconnected unless we
            // already moved to Error.
            lock (_gate)
            {
                if (_state == RpcConnectionState.Connected || _state == RpcConnectionState.Connecting)
                    SetState(RpcConnectionState.Disconnected, "Worker exited.");
            }
        }
    }

    private void HandleEvent(WorkerEvent evt)
    {
        switch (evt.Event)
        {
            case "state":
                var state = evt.State switch
                {
                    "Connecting" => RpcConnectionState.Connecting,
                    "Connected" => RpcConnectionState.Connected,
                    "Disconnected" => RpcConnectionState.Disconnected,
                    "Error" => RpcConnectionState.Error,
                    _ => _state,
                };
                if (evt.Username is not null) Username = evt.Username;
                if (evt.AvatarUrl is not null) AvatarUrl = evt.AvatarUrl;
                if (state == RpcConnectionState.Error && evt.Message is not null)
                    LastError = evt.Message;
                SetState(state, evt.Message, Username, AvatarUrl);
                break;

            case "sent":
                LastSentAt = DateTime.Now;
                LastSentSummary = evt.Summary ?? "Presence active";
                LastError = null;
                PresenceSent?.Invoke(this, LastSentSummary);
                break;

            case "failed":
                LastError = evt.Message ?? "Unknown error";
                PresenceFailed?.Invoke(this, LastError);
                break;
        }
    }

    private void SetState(RpcConnectionState state, string? message,
        string? username = null, string? avatarUrl = null)
    {
        _state = state;
        StatusMessage = message;
        if (state == RpcConnectionState.Error && message is not null)
            LastError ??= message;
        StateChanged?.Invoke(this, new RpcStateChangedEventArgs(state, message, username, avatarUrl));
    }

    private void StopWorker()
    {
        var proc = _proc;
        var cts = _readerCts;
        _proc = null;
        _readerCts = null;

        cts?.Cancel();
        cts?.Dispose();

        if (proc is null) return;

        try
        {
            if (!proc.HasExited)
            {
                try
                {
                    proc.StandardInput.WriteLine(JsonSerializer.Serialize(
                        new WorkerCommand { Cmd = "stop" }, WorkerJson.Options));
                    proc.StandardInput.Flush();
                }
                catch { /* worker may already be gone */ }

                try { proc.StandardInput.Close(); } catch { }

                if (!proc.WaitForExit(1500))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        catch { /* swallow — disposing */ }
        finally
        {
            try { proc.Dispose(); } catch { }
        }
    }
}
