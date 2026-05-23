using CustomRP.Modern.Models;
using System;
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

    /// <summary>
    /// Resolves the Client ID a preset should connect with. Preset's explicit
    /// ClientId wins; otherwise the per-category ID from Settings is used so
    /// that two presets in different categories never collide on the default
    /// app and overwrite each other in Discord.
    /// </summary>
    private string ResolveClientId(Preset preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.ClientId))
            return preset.ClientId.Trim();

        var category = preset.Metadata?.Category ?? "";
        if (!string.IsNullOrEmpty(category)
            && _settings.Current.CategoryClientIds.TryGetValue(category, out var catId)
            && !string.IsNullOrWhiteSpace(catId))
            return catId.Trim();

        return PresencePayloadBuilder.DefaultClientId;
    }
}
