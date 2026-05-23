using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomRP.Modern.Services;
using System.Linq;

namespace CustomRP.Modern.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public PresenceEditorViewModel Editor { get; }
    public PresetLibraryViewModel Library { get; }
    public AppDetectViewModel Detect { get; }
    public SettingsViewModel Settings { get; }
    public ActiveConnectionsViewModel Active { get; }

    [ObservableProperty]
    private string _selectedSection = "Editor";

    [ObservableProperty]
    private string _statusMessage = "Idle";

    [ObservableProperty]
    private string _statusKind = "Idle"; // Idle | Connecting | Connected | Error

    [ObservableProperty]
    private string? _connectedUsername;

    [ObservableProperty]
    private string? _connectedAvatarUrl;

    [ObservableProperty]
    private int _activeCount;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        Editor = new PresenceEditorViewModel(services);
        Library = new PresetLibraryViewModel(services, Editor, () => SelectedSection = "Editor");
        Detect = new AppDetectViewModel(services, Editor, () => SelectedSection = "Editor");
        Settings = new SettingsViewModel(services);
        Active = new ActiveConnectionsViewModel(services, Editor, () => SelectedSection = "Editor");

        services.Connections.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshAggregateStatus);
    }

    private void RefreshAggregateStatus()
    {
        var connections = _services.Connections.Connections;
        ActiveCount = connections.Count;

        var connected = connections.FirstOrDefault(c => c.State == RpcConnectionState.Connected);
        if (connected is not null)
        {
            StatusKind = "Connected";
            StatusMessage = connections.Count == 1
                ? $"Connected as {connected.Username}"
                : $"{connections.Count(c => c.State == RpcConnectionState.Connected)} of {connections.Count} active";
            ConnectedUsername = connected.Username;
            ConnectedAvatarUrl = connected.AvatarUrl;
            return;
        }

        var failed = connections.FirstOrDefault(c => c.State == RpcConnectionState.Error);
        if (failed is not null)
        {
            StatusKind = "Error";
            StatusMessage = failed.LastError ?? failed.StatusMessage ?? "Connection failed";
            return;
        }

        var connecting = connections.FirstOrDefault(c => c.State == RpcConnectionState.Connecting);
        if (connecting is not null)
        {
            StatusKind = "Connecting";
            StatusMessage = "Connecting…";
            return;
        }

        StatusKind = "Idle";
        StatusMessage = connections.Count == 0 ? "Idle" : $"{connections.Count} preset(s) loaded";
        ConnectedUsername = null;
        ConnectedAvatarUrl = null;
    }

    [RelayCommand] private void NavigateEditor() => SelectedSection = "Editor";
    [RelayCommand] private void NavigateLibrary() => SelectedSection = "Library";
    [RelayCommand] private void NavigateDetect() => SelectedSection = "Detect";
    [RelayCommand] private void NavigateActive() => SelectedSection = "Active";
    [RelayCommand] private void NavigateSettings() => SelectedSection = "Settings";
}
