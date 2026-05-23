using CustomRP.Modern.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CustomRP.Modern.Services;

/// <summary>
/// Owns the set of live Discord RPC connections. Each connection is keyed by
/// its Client ID — starting the same Client ID twice replaces the existing
/// connection. The editor and the library "Start as background" command both
/// route through here, which is how the app supports multiple simultaneous
/// rich presences.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly SettingsService _settings;

    public ObservableCollection<RpcConnection> Connections { get; } = new();

    public ConnectionManager(SettingsService settings)
    {
        _settings = settings;
    }

    public event EventHandler? Changed;
    public event EventHandler<RpcConnection>? ConnectionAdded;
    public event EventHandler<RpcConnection>? ConnectionRemoved;

    public RpcConnection? Find(string clientId) =>
        Connections.FirstOrDefault(c => c.ClientId == clientId);

    /// <summary>
    /// Starts a connection for the given preset. If one already exists with
    /// the same Client ID it is replaced (preserving identity inside the
    /// observable collection is not worth the complexity here).
    /// </summary>
    public RpcConnection Start(Preset preset, string? displayName = null)
    {
        preset.ClientId = ResolveClientId(preset);
        var clientId = preset.ClientId;
        var existing = Find(clientId);
        if (existing is not null)
        {
            existing.Preset = preset;
            existing.DisplayName = displayName ?? preset.Metadata.Name;
            existing.Pipe = _settings.Current.DiscordPipe;
            existing.Connect();
            Changed?.Invoke(this, EventArgs.Empty);
            return existing;
        }

        var conn = new RpcConnection(
            clientId,
            displayName ?? preset.Metadata.Name,
            preset)
        {
            Pipe = _settings.Current.DiscordPipe,
        };

        conn.StateChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        conn.PresenceSent += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        conn.PresenceFailed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        Connections.Add(conn);
        ConnectionAdded?.Invoke(this, conn);

        conn.Connect();
        return conn;
    }

    public void Stop(string clientId)
    {
        var conn = Find(clientId);
        if (conn is null) return;
        conn.Disconnect();
        Connections.Remove(conn);
        conn.Dispose();
        ConnectionRemoved?.Invoke(this, conn);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void StopAll()
    {
        foreach (var conn in Connections.ToList())
            Stop(conn.ClientId);
    }

    public void Dispose() => StopAll();

    // Pre-registered Discord Applications used as generic connection slots so
    // multiple presences can run simultaneously without the user needing to
    // create their own applications. Discord shows one activity per Application ID.
    private static readonly string[] BuiltInSlots =
    {
        "1507440289390919882",
        "1507794956465475805",
        "1507795285836038267",
        "1507795526610059386",
        "1507795792591589436",
        "1507796028361932830",
        "1507796396378554578",
        "1507796978992418908",
    };

    /// <summary>
    /// Resolves the Client ID a preset should connect with.
    /// If the preset has an explicit ID that differs from the global default,
    /// it is used directly. Otherwise the first unused slot from the built-in
    /// pool is picked so that multiple presences never collide.
    /// </summary>
    private string ResolveClientId(Preset preset)
    {
        var def = !string.IsNullOrWhiteSpace(_settings.Current.DefaultClientId)
            ? _settings.Current.DefaultClientId.Trim()
            : PresencePayloadBuilder.DefaultClientId;

        var explicit_ = preset.ClientId?.Trim() ?? "";

        // Explicit per-preset ID that the user intentionally set (not just the
        // default value echoed back) is used as-is.
        if (!string.IsNullOrEmpty(explicit_) && explicit_ != def)
            return explicit_;

        // Auto-slot: pick the first ID in the pool that isn't already hosting
        // a live connection so multiple presences can coexist.
        var used = new HashSet<string>(Connections.Select(c => c.ClientId),
            StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(def))
            return def;

        foreach (var slot in BuiltInSlots)
            if (!used.Contains(slot))
                return slot;

        return def; // All slots occupied — caller will replace the default connection.
    }
}
