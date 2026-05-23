using CustomRP.Modern.Models;
using DiscordRPC;
using DiscordRPC.Logging;
using System;
using System.Threading;

namespace CustomRP.Modern.Services;

/// <summary>
/// One Discord RPC connection bound to one Client ID, running inside a worker
/// child process. Events are emitted to the parent process via the
/// <see cref="WorkerEvent"/> protocol — no Avalonia dispatcher is available
/// here, so all callbacks serialize through an internal lock instead.
/// </summary>
public sealed class DiscordRpcSession : IDisposable
{
    private readonly string _clientId;
    private readonly int _pipe;
    private readonly Action<WorkerEvent> _emit;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private DiscordRpcClient? _client;
    private Timer? _keepAlive;
    private Preset _preset;
    private bool _disposed;

    public DiscordRpcSession(
        string clientId,
        int pipe,
        Preset initialPreset,
        Action<WorkerEvent> emit,
        ILogger logger)
    {
        _clientId = clientId;
        _pipe = pipe;
        _preset = initialPreset;
        _emit = emit;
        _logger = logger;
    }

    public void Connect()
    {
        lock (_gate)
        {
            if (_disposed) return;
            DisposeClient();

            EmitState("Connecting", "Connecting to Discord…");

            var client = new DiscordRpcClient(_clientId, _pipe)
            {
                Logger = _logger,
                SkipIdenticalPresence = false,
            };
            _client = client;

            client.OnReady += (_, msg) =>
            {
                lock (_gate)
                {
                    if (_disposed || _client != client) return;
                    var username = msg.User.Username;
                    var avatar = msg.User.GetAvatarURL(User.AvatarFormat.PNG);
                    EmitState("Connected", $"Connected as {username}", username, avatar);
                    Thread.Sleep(150); // Matches legacy ordering after Ready.
                    TrySendPresence(client, _preset);
                    StartKeepAlive(client);
                }
            };

            client.OnPresenceUpdate += (_, _) =>
            {
                _emit(new WorkerEvent { Event = "sent", Summary = "Presence active" });
            };

            client.OnConnectionFailed += (_, _) =>
                EmitState("Error", "Discord pipe unavailable (is Discord running?)");

            client.OnError += (_, msg) =>
            {
                var text = $"{msg.Code}: {msg.Message}";
                _emit(new WorkerEvent { Event = "failed", Message = text });
                EmitState("Error", text);
            };

            client.OnClose += (_, _) =>
            {
                lock (_gate)
                {
                    StopKeepAlive();
                    EmitState("Disconnected", "Connection closed.");
                }
            };

            if (!client.Initialize())
                EmitState("Error", "Could not open Discord IPC pipe (is Discord running?)");
        }
    }

    public void UpdatePresence(Preset preset)
    {
        lock (_gate)
        {
            _preset = preset;
            if (_client is null || _client.IsDisposed)
            {
                _emit(new WorkerEvent { Event = "failed", Message = "Not connected to Discord." });
                return;
            }
            TrySendPresence(_client, preset);
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            StopKeepAlive();
            DisposeClient();
            EmitState("Disconnected", "Disconnected.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopKeepAlive();
            DisposeClient();
        }
    }

    private void TrySendPresence(DiscordRpcClient client, Preset preset)
    {
        try
        {
            var presence = PresencePayloadBuilder.Build(preset);
            client.SetPresence(presence);
            var summary = PresencePayloadBuilder.Summarize(presence);
            _emit(new WorkerEvent { Event = "sent", Summary = summary });
        }
        catch (Exception ex)
        {
            _emit(new WorkerEvent { Event = "failed", Message = ex.Message });
            EmitState("Error", ex.Message);
        }
    }

    private void StartKeepAlive(DiscordRpcClient client)
    {
        StopKeepAlive();
        _keepAlive = new Timer(_ =>
        {
            lock (_gate)
            {
                if (_disposed || _client != client || client.IsDisposed) return;
                TrySendPresence(client, _preset);
            }
        }, null, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90));
    }

    private void StopKeepAlive()
    {
        _keepAlive?.Dispose();
        _keepAlive = null;
    }

    private void DisposeClient()
    {
        if (_client is null) return;
        var c = _client;
        _client = null;
        if (!c.IsDisposed)
        {
            try { c.ClearPresence(); } catch { }
            try { c.Dispose(); } catch { }
        }
    }

    private void EmitState(string state, string message, string? username = null, string? avatar = null)
    {
        _emit(new WorkerEvent
        {
            Event = "state",
            State = state,
            Message = message,
            Username = username,
            AvatarUrl = avatar,
        });
    }
}
