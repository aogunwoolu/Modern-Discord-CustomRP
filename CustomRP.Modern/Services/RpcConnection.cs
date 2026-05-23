using CustomRP.Modern.Models;
using DiscordRPC;
using DiscordRPC.Logging;
using System;
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
/// A single live Discord RPC connection bound to one Client ID. Owns its own
/// <see cref="DiscordRpcClient"/> and surfaces state via events. The
/// <see cref="ConnectionManager"/> aggregates multiple of these — letting the
/// user run several presences at once.
/// </summary>
public sealed class RpcConnection : IDisposable
{
    private readonly ILogger _logger;
    private DiscordRpcClient? _client;
    private Timer? _keepAlive;
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

    public RpcConnection(string clientId, string displayName, Preset preset, ILogger logger)
    {
        ClientId = clientId;
        DisplayName = displayName;
        Preset = preset;
        _logger = logger;
    }

    public void Connect()
    {
        Disconnect();
        UiDispatcher.Invoke(() => SetState(RpcConnectionState.Connecting, "Connecting to Discord…"));

        _client = new DiscordRpcClient(ClientId, Pipe)
        {
            Logger = _logger,
            SkipIdenticalPresence = false,
        };

        _client.OnReady += (_, msg) =>
        {
            UiDispatcher.Post(async () =>
            {
                Username = msg.User.Username;
                AvatarUrl = msg.User.GetAvatarURL(User.AvatarFormat.PNG);
                SetState(RpcConnectionState.Connected,
                    $"Connected as {Username}", Username, AvatarUrl);
                // Brief delay matches legacy WinForms Invoke ordering after Ready.
                await Task.Delay(150);
                if (_client is { IsDisposed: false })
                    TrySendPresence(_client, Preset);
                StartKeepAlive();
            });
        };

        _client.OnPresenceUpdate += (_, _) =>
        {
            UiDispatcher.Post(() =>
            {
                LastError = null;
                PresenceSent?.Invoke(this, LastSentSummary ?? "Presence active");
            });
        };

        _client.OnConnectionFailed += (_, _) =>
            UiDispatcher.Post(() => SetState(RpcConnectionState.Error,
                "Discord pipe unavailable (is Discord running?)"));

        _client.OnError += (_, msg) =>
        {
            UiDispatcher.Post(() =>
            {
                LastError = $"{msg.Code}: {msg.Message}";
                PresenceFailed?.Invoke(this, LastError);
                SetState(RpcConnectionState.Error, LastError);
            });
        };

        _client.OnClose += (_, _) =>
        {
            UiDispatcher.Post(() =>
            {
                StopKeepAlive();
                if (_state == RpcConnectionState.Connected)
                    SetState(RpcConnectionState.Disconnected, "Connection closed.");
            });
        };

        if (!_client.Initialize())
            UiDispatcher.Post(() => SetState(RpcConnectionState.Error,
                "Could not open Discord IPC pipe (is Discord running?)"));
    }

    public void Disconnect()
    {
        StopKeepAlive();
        if (_client is { IsDisposed: false })
        {
            try { _client.ClearPresence(); } catch { /* ignore */ }
            try { _client.Dispose(); } catch { /* ignore */ }
        }
        _client = null;
        if (_state != RpcConnectionState.Disconnected)
            SetState(RpcConnectionState.Disconnected, "Disconnected.");
    }

    public void UpdatePresence(Preset preset)
    {
        Preset = preset;

        if (_client is not { IsDisposed: false } client)
        {
            const string msg = "Not connected to Discord.";
            LastError = msg;
            PresenceFailed?.Invoke(this, msg);
            return;
        }

        if (_state != RpcConnectionState.Connected)
        {
            const string msg = "Waiting for Discord handshake…";
            LastError = msg;
            PresenceFailed?.Invoke(this, msg);
            return;
        }

        UiDispatcher.Invoke(() => TrySendPresence(client, preset));
    }

    private void StartKeepAlive()
    {
        StopKeepAlive();
        _keepAlive = new Timer(_ =>
        {
            if (_client is { IsDisposed: false } c && _state == RpcConnectionState.Connected)
                UiDispatcher.Post(() => TrySendPresence(c, Preset));
        }, null, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90));
    }

    private void StopKeepAlive()
    {
        _keepAlive?.Dispose();
        _keepAlive = null;
    }

    private void TrySendPresence(DiscordRpcClient client, Preset preset)
    {
        try
        {
            var presence = PresencePayloadBuilder.Build(preset);
            client.SetPresence(presence);
            var summary = PresencePayloadBuilder.Summarize(presence);
            LastSentAt = DateTime.Now;
            LastSentSummary = summary;
            LastError = null;
            PresenceSent?.Invoke(this, summary);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            PresenceFailed?.Invoke(this, ex.Message);
            SetState(RpcConnectionState.Error, ex.Message);
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

    public void Dispose() => Disconnect();
}
