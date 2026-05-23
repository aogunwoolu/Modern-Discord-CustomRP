using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomRP.Modern.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace CustomRP.Modern.ViewModels;

public partial class ActiveConnectionsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public ObservableCollection<RpcConnection> Connections { get; } = new();

    [ObservableProperty] private string _emptyHint =
        "No active presences. Connect from the Editor or hit \"Start as background\" on any preset in the Library.";

    public ActiveConnectionsViewModel(AppServices services)
    {
        _services = services;
        Sync();
        services.Connections.Changed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(Sync);
    }

    private void Sync()
    {
        var live = _services.Connections.Connections.ToList();

        // Replace contents while preserving the user's scroll position when possible.
        for (int i = Connections.Count - 1; i >= 0; i--)
        {
            if (!live.Contains(Connections[i]))
                Connections.RemoveAt(i);
        }
        foreach (var c in live)
        {
            if (!Connections.Contains(c))
                Connections.Add(c);
        }
        OnPropertyChanged(nameof(Connections));
    }

    [RelayCommand]
    private void Stop(RpcConnection? conn)
    {
        if (conn is null) return;
        _services.Connections.Stop(conn.ClientId);
    }

    [RelayCommand]
    private void Reconnect(RpcConnection? conn)
    {
        if (conn is null) return;
        conn.Connect();
    }

    [RelayCommand]
    private void StopAll() => _services.Connections.StopAll();
}
