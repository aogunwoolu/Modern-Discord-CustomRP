using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomRP.Modern.Models;
using CustomRP.Modern.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CustomRP.Modern.ViewModels;

public partial class PresenceEditorViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private CancellationTokenSource? _presenceDebounce;
    /// <summary>
    /// The exact <see cref="Preset"/> instance this editor passed to
    /// <see cref="ConnectionManager.Start"/>. Used to detect whether the live
    /// connection for this ClientId belongs to *this* editor activity or to a
    /// different one loaded from the library / auto-detect.
    /// </summary>
    private Preset? _connectedPreset;

    public IReadOnlyList<ActivityKind> ActivityKinds { get; } =
        new[] { ActivityKind.Playing, ActivityKind.Listening, ActivityKind.Watching, ActivityKind.Competing };

    public IReadOnlyList<DisplayMode> DisplayModes { get; } =
        new[] { DisplayMode.Name, DisplayMode.State, DisplayMode.Details };

    public IReadOnlyList<TimestampMode> TimestampModes { get; } =
        Enum.GetValues<TimestampMode>();

    [ObservableProperty] private Preset _preset = new();
    [ObservableProperty] private string _clientId = "";
    [ObservableProperty] private string _activityName = "";
    [ObservableProperty] private ActivityKind _activityType = ActivityKind.Playing;
    [ObservableProperty] private DisplayMode _displayMode = DisplayMode.Name;
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private int _partySize;
    [ObservableProperty] private int _partyMax;
    [ObservableProperty] private TimestampMode _timestamps = TimestampMode.None;
    [ObservableProperty] private DateTime _customTimestampStart = DateTime.Now;
    [ObservableProperty] private bool _customTimestampEndEnabled;
    [ObservableProperty] private DateTime _customTimestampEnd = DateTime.Now.AddHours(1);
    [ObservableProperty] private string _largeImageKey = "";
    [ObservableProperty] private string _largeImageText = "";
    [ObservableProperty] private string _smallImageKey = "";
    [ObservableProperty] private string _smallImageText = "";
    [ObservableProperty] private string _button1Text = "";
    [ObservableProperty] private string _button1Url = "";
    [ObservableProperty] private string _button2Text = "";
    [ObservableProperty] private string _button2Url = "";

    [ObservableProperty] private string _presetName = "Untitled preset";
    [ObservableProperty] private string _presetAuthor = "";
    [ObservableProperty] private string _presetDescription = "";
    [ObservableProperty] private string _presetCategory = "";

    /// <summary>Category options available in the editor's category picker —
    /// "(none)" plus every key the user has configured in Settings.</summary>
    public IReadOnlyList<string> CategoryOptions =>
        new[] { "(none)" }
            .Concat(_services.Settings.Current.CategoryClientIds.Keys.OrderBy(k => k))
            .ToList();

    [ObservableProperty] private string _connectButtonLabel = "Connect";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _lastSentLabel = "Nothing sent yet";
    [ObservableProperty] private bool _lastSentIsError;
    /// <summary>Read-only label shown in the editor explaining which Client ID is in use.</summary>
    [ObservableProperty] private string _clientIdSourceLabel = "";

    // ---- Auto-update ----
    public IReadOnlyList<AutoUpdateStrategy> AutoUpdateStrategies { get; } =
        new[] { AutoUpdateStrategy.Off, AutoUpdateStrategy.WindowTitle, AutoUpdateStrategy.BrowserUrl };

    [ObservableProperty] private bool _autoUpdateEnabled;
    [ObservableProperty] private AutoUpdateStrategy _autoUpdateStrategy = AutoUpdateStrategy.WindowTitle;
    [ObservableProperty] private string _autoUpdateProcessName = "";
    [ObservableProperty] private int _autoUpdateInterval = 3;
    [ObservableProperty] private string _autoUpdateDetailsTemplate = "{title}";
    [ObservableProperty] private string _autoUpdateStateTemplate = "";
    [ObservableProperty] private bool _autoUpdateUseFavicon;
    [ObservableProperty] private string _autoUpdateButton1TextTemplate = "";
    [ObservableProperty] private string _autoUpdateButton1UrlTemplate = "";
    [ObservableProperty] private string _autoUpdateButton2TextTemplate = "";
    [ObservableProperty] private string _autoUpdateButton2UrlTemplate = "";
    [ObservableProperty] private string _liveStatusLabel = "Auto-update idle";

    // What the preview shows. Mirrors static fields when auto-update is off,
    // otherwise reflects the live values rendered from a snapshot.
    [ObservableProperty] private string _effectiveDetails = "";
    [ObservableProperty] private string _effectiveState = "";
    [ObservableProperty] private string _effectiveSmallImageKey = "";
    [ObservableProperty] private string _effectiveButton1Text = "";
    [ObservableProperty] private string _effectiveButton1Url = "";
    [ObservableProperty] private string _effectiveButton2Text = "";
    [ObservableProperty] private string _effectiveButton2Url = "";

    public IEnumerable<string> AutoUpdateProcessSuggestions =>
        _services.KnownApps.Apps
            .SelectMany(a => a.ProcessNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p);

    public PresenceEditorViewModel(AppServices services)
    {
        _services = services;
        if (!string.IsNullOrEmpty(services.Settings.Current.LastClientId))
            ClientId = services.Settings.Current.LastClientId;

        services.Connections.Changed += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(SyncConnectionState);
        };

        // Adopt any connection that already exists for this ClientId when the
        // app restores active presets on startup (before the editor had a chance
        // to set _connectedPreset via ToggleConnection).
        services.Connections.ConnectionAdded += (_, conn) =>
        {
            if (_connectedPreset is null && conn.ClientId == EffectiveClientId)
                _connectedPreset = conn.Preset;
        };

        services.AutoUpdate.Updated += (_, snapshot) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
        };

        // Whenever any presence-relevant property changes and we are connected,
        // push the updated presence. PropertyChanged covers all generated props.
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null) return;

            // Auto-update config changes — reconfigure the watcher.
            if (args.PropertyName is nameof(AutoUpdateEnabled)
                or nameof(AutoUpdateStrategy)
                or nameof(AutoUpdateProcessName)
                or nameof(AutoUpdateInterval)
                or nameof(AutoUpdateDetailsTemplate)
                or nameof(AutoUpdateStateTemplate)
                or nameof(AutoUpdateUseFavicon)
                or nameof(AutoUpdateButton1TextTemplate)
                or nameof(AutoUpdateButton1UrlTemplate)
                or nameof(AutoUpdateButton2TextTemplate)
                or nameof(AutoUpdateButton2UrlTemplate))
            {
                ReconfigureAutoUpdate();
            }

            // Mirror static fields to effective fields when auto-update is off.
            if (!AutoUpdateEnabled)
            {
                if (args.PropertyName == nameof(Details)) EffectiveDetails = Details;
                if (args.PropertyName == nameof(State)) EffectiveState = State;
                if (args.PropertyName == nameof(SmallImageKey)) EffectiveSmallImageKey = SmallImageKey;
                if (args.PropertyName == nameof(Button1Text)) EffectiveButton1Text = Button1Text;
                if (args.PropertyName == nameof(Button1Url)) EffectiveButton1Url = Button1Url;
                if (args.PropertyName == nameof(Button2Text)) EffectiveButton2Text = Button2Text;
                if (args.PropertyName == nameof(Button2Url)) EffectiveButton2Url = Button2Url;
            }

            if (args.PropertyName is nameof(ConnectButtonLabel)
                or nameof(IsConnected)
                or nameof(IsDirty)
                or nameof(LiveStatusLabel)
                or nameof(LastSentLabel)
                or nameof(LastSentIsError)
                or nameof(EffectiveDetails)
                or nameof(EffectiveState)
                or nameof(EffectiveSmallImageKey)
                or nameof(EffectiveButton1Text)
                or nameof(EffectiveButton1Url)
                or nameof(EffectiveButton2Text)
                or nameof(EffectiveButton2Url))
                return;

            IsDirty = true;
            if (IsConnected) SchedulePushPresence();
        };
    }

    private void ApplySnapshot(LiveSnapshot snapshot)
    {
        if (!AutoUpdateEnabled) return;

        if (!snapshot.ProcessFound)
        {
            LiveStatusLabel = $"Waiting for '{AutoUpdateProcessName}'…";
            return;
        }

        EffectiveDetails = TemplateRenderer.Render(AutoUpdateDetailsTemplate, snapshot);
        EffectiveState = TemplateRenderer.Render(AutoUpdateStateTemplate, snapshot);
        if (AutoUpdateUseFavicon && !string.IsNullOrEmpty(snapshot.FaviconUrl))
            EffectiveSmallImageKey = snapshot.FaviconUrl;

        // Button templates — fall back to static button values when template is empty.
        EffectiveButton1Text = string.IsNullOrWhiteSpace(AutoUpdateButton1TextTemplate)
            ? Button1Text
            : TemplateRenderer.Render(AutoUpdateButton1TextTemplate, snapshot);
        EffectiveButton1Url = string.IsNullOrWhiteSpace(AutoUpdateButton1UrlTemplate)
            ? Button1Url
            : TemplateRenderer.Render(AutoUpdateButton1UrlTemplate, snapshot);
        EffectiveButton2Text = string.IsNullOrWhiteSpace(AutoUpdateButton2TextTemplate)
            ? Button2Text
            : TemplateRenderer.Render(AutoUpdateButton2TextTemplate, snapshot);
        EffectiveButton2Url = string.IsNullOrWhiteSpace(AutoUpdateButton2UrlTemplate)
            ? Button2Url
            : TemplateRenderer.Render(AutoUpdateButton2UrlTemplate, snapshot);

        LiveStatusLabel = snapshot.Url is { Length: > 0 }
            ? $"Watching {snapshot.ProcessName}: {snapshot.Url}"
            : $"Watching {snapshot.ProcessName}: {snapshot.WindowTitle}";

        if (IsConnected) SchedulePushPresence();
    }

    private void ReconfigureAutoUpdate()
    {
        _services.AutoUpdate.Configure(new AutoUpdateConfig
        {
            Enabled = AutoUpdateEnabled,
            Strategy = AutoUpdateStrategy,
            ProcessName = AutoUpdateProcessName,
            IntervalSeconds = Math.Max(1, AutoUpdateInterval),
            DetailsTemplate = AutoUpdateDetailsTemplate,
            StateTemplate = AutoUpdateStateTemplate,
            UseFaviconAsSmallImage = AutoUpdateUseFavicon,
            Button1TextTemplate = AutoUpdateButton1TextTemplate,
            Button1UrlTemplate = AutoUpdateButton1UrlTemplate,
            Button2TextTemplate = AutoUpdateButton2TextTemplate,
            Button2UrlTemplate = AutoUpdateButton2UrlTemplate,
        });

        if (!AutoUpdateEnabled)
        {
            EffectiveDetails = Details;
            EffectiveState = State;
            EffectiveSmallImageKey = SmallImageKey;
            EffectiveButton1Text = Button1Text;
            EffectiveButton1Url = Button1Url;
            EffectiveButton2Text = Button2Text;
            EffectiveButton2Url = Button2Url;
            LiveStatusLabel = "Auto-update idle";
        }
        else
        {
            LiveStatusLabel = $"Watching '{AutoUpdateProcessName}'…";
        }
    }

    public void LoadPreset(Preset preset)
    {
        _connectedPreset = null; // This editor no longer owns whatever was running.
        Preset = preset;
        ClientId = preset.ClientId;
        ActivityType = preset.Type;
        DisplayMode = preset.Display;
        ActivityName = preset.ActivityName;
        Details = preset.Details;
        State = preset.State;
        PartySize = preset.PartySize;
        PartyMax = preset.PartyMax;
        Timestamps = preset.Timestamps;
        CustomTimestampStart = preset.CustomTimestampStart.ToLocalTime();
        CustomTimestampEndEnabled = preset.CustomTimestampEndEnabled;
        CustomTimestampEnd = preset.CustomTimestampEnd.ToLocalTime();
        LargeImageKey = preset.LargeImage.Key;
        LargeImageText = preset.LargeImage.Text;
        SmallImageKey = preset.SmallImage.Key;
        SmallImageText = preset.SmallImage.Text;

        var b = preset.Buttons;
        Button1Text = b.Count > 0 ? b[0].Text : "";
        Button1Url = b.Count > 0 ? b[0].Url : "";
        Button2Text = b.Count > 1 ? b[1].Text : "";
        Button2Url = b.Count > 1 ? b[1].Url : "";

        PresetName = preset.Metadata.Name;
        PresetAuthor = preset.Metadata.Author;
        PresetDescription = preset.Metadata.Description;
        PresetCategory = string.IsNullOrWhiteSpace(preset.Metadata.Category) ? "(none)" : preset.Metadata.Category;

        var au = preset.AutoUpdate;
        AutoUpdateEnabled = au.Enabled;
        AutoUpdateStrategy = au.Strategy;
        AutoUpdateProcessName = au.ProcessName;
        AutoUpdateInterval = Math.Max(1, au.IntervalSeconds);
        AutoUpdateDetailsTemplate = au.DetailsTemplate;
        AutoUpdateStateTemplate = au.StateTemplate;
        AutoUpdateUseFavicon = au.UseFaviconAsSmallImage;
        AutoUpdateButton1TextTemplate = au.Button1TextTemplate;
        AutoUpdateButton1UrlTemplate = au.Button1UrlTemplate;
        AutoUpdateButton2TextTemplate = au.Button2TextTemplate;
        AutoUpdateButton2UrlTemplate = au.Button2UrlTemplate;

        EffectiveDetails = AutoUpdateEnabled ? "" : Details;
        EffectiveState = AutoUpdateEnabled ? "" : State;
        EffectiveSmallImageKey = AutoUpdateEnabled ? "" : SmallImageKey;
        EffectiveButton1Text = Button1Text;
        EffectiveButton1Url = Button1Url;
        EffectiveButton2Text = Button2Text;
        EffectiveButton2Url = Button2Url;

        IsDirty = false;
        SyncConnectionState(); // Refresh Connect/Disconnect and ClientIdSourceLabel for the new preset.
    }

    public Preset BuildPreset() => new()
    {
        ClientId = EffectiveClientId,
        Type = ActivityType,
        Display = DisplayMode,
        ActivityName = ActivityName,
        Details = Details,
        State = State,
        PartySize = PartySize,
        PartyMax = PartyMax,
        Timestamps = Timestamps,
        CustomTimestampStart = CustomTimestampStart.ToUniversalTime(),
        CustomTimestampEndEnabled = CustomTimestampEndEnabled,
        CustomTimestampEnd = CustomTimestampEnd.ToUniversalTime(),
        LargeImage = new ImageAsset { Key = LargeImageKey, Text = LargeImageText },
        SmallImage = new ImageAsset { Key = SmallImageKey, Text = SmallImageText },
        Buttons = new List<PresenceButton>
        {
            new() { Text = Button1Text, Url = Button1Url },
            new() { Text = Button2Text, Url = Button2Url },
        },
        AutoUpdate = new AutoUpdateConfig
        {
            Enabled = AutoUpdateEnabled,
            Strategy = AutoUpdateStrategy,
            ProcessName = AutoUpdateProcessName,
            IntervalSeconds = Math.Max(1, AutoUpdateInterval),
            DetailsTemplate = AutoUpdateDetailsTemplate,
            StateTemplate = AutoUpdateStateTemplate,
            UseFaviconAsSmallImage = AutoUpdateUseFavicon,
            Button1TextTemplate = AutoUpdateButton1TextTemplate,
            Button1UrlTemplate = AutoUpdateButton1UrlTemplate,
            Button2TextTemplate = AutoUpdateButton2TextTemplate,
            Button2UrlTemplate = AutoUpdateButton2UrlTemplate,
        },
        Metadata = new PresetMetadata
        {
            Name = string.IsNullOrWhiteSpace(PresetName) ? "Untitled preset" : PresetName,
            Author = PresetAuthor,
            Description = PresetDescription,
            Tags = Preset?.Metadata?.Tags is { Count: > 0 } t ? new List<string>(t) : new(),
            IconUrl = Preset?.Metadata?.IconUrl ?? "",
            Category = PresetCategory == "(none)" ? "" : PresetCategory,
            Created = Preset?.Metadata?.Created ?? DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        },
    };

    /// <summary>
    /// Builds the preset that should be sent to Discord *right now*. When
    /// auto-update is active this overrides Details / State / SmallImage with
    /// values derived from the live snapshot.
    /// </summary>
    private Preset BuildEffectivePreset()
    {
        var preset = BuildPreset();
        if (!AutoUpdateEnabled) return preset;

        preset.Details = EffectiveDetails;
        preset.State = EffectiveState;
        if (!string.IsNullOrWhiteSpace(EffectiveSmallImageKey))
        {
            preset.SmallImage = new ImageAsset
            {
                Key = EffectiveSmallImageKey,
                Text = preset.SmallImage.Text,
            };
        }
        preset.Buttons = new List<PresenceButton>
        {
            new() { Text = EffectiveButton1Text, Url = EffectiveButton1Url },
            new() { Text = EffectiveButton2Text, Url = EffectiveButton2Url },
        };
        return preset;
    }

    /// <summary>Resolves the live category from the editor's UI — normalizes
    /// the "(none)" sentinel back to empty.</summary>
    private string CurrentCategory =>
        string.IsNullOrEmpty(PresetCategory) || PresetCategory == "(none)" ? "" : PresetCategory;

    partial void OnPresetCategoryChanged(string value) => SyncConnectionState();
    partial void OnClientIdChanged(string value) => SyncConnectionState();

    /// <summary>
    /// The Client ID that will actually be used when connecting. Category setting
    /// takes priority over the per-preset ClientId field.
    /// </summary>
    private string EffectiveClientId
    {
        get
        {
            var category = CurrentCategory;
            if (!string.IsNullOrEmpty(category)
                && _services.Settings.Current.CategoryClientIds.TryGetValue(category, out var catId)
                && !string.IsNullOrWhiteSpace(catId))
                return catId.Trim();

            return string.IsNullOrWhiteSpace(ClientId)
                ? PresencePayloadBuilder.DefaultClientId
                : ClientId.Trim();
        }
    }

    private RpcConnection? EditorConnection =>
        _services.Connections.Find(EffectiveClientId);

    private void SyncConnectionState()
    {
        var conn = EditorConnection;
        // Only "connected" when the running connection for this ClientId was
        // started by *this* editor instance (same Preset reference).
        var ownsConn = conn is not null && ReferenceEquals(conn.Preset, _connectedPreset);
        IsConnected = ownsConn && conn!.State == RpcConnectionState.Connected;
        ConnectButtonLabel = IsConnected ? "Disconnect" : "Connect";

        // Explain which Client ID is in use.
        var category = Preset?.Metadata?.Category ?? "";
        if (!string.IsNullOrEmpty(category)
            && _services.Settings.Current.CategoryClientIds.TryGetValue(category, out var catId)
            && !string.IsNullOrWhiteSpace(catId))
            ClientIdSourceLabel = $"Using {category} application ID from Settings ({catId.Trim()})";
        else
            ClientIdSourceLabel = string.IsNullOrWhiteSpace(ClientId)
                ? $"Using default application ID ({PresencePayloadBuilder.DefaultClientId})"
                : $"Using custom application ID ({ClientId.Trim()})";

        if (conn is null)
        {
            LastSentLabel = "Nothing sent yet";
            LastSentIsError = false;
            return;
        }

        // Same ClientId is running a *different* activity — inform the user.
        if (!ownsConn)
        {
            LastSentIsError = false;
            LastSentLabel = $"Client ID already used by \u2018{conn.DisplayName}\u2019. Click Connect to replace it.";
            return;
        }

        if (conn.State == RpcConnectionState.Error)
        {
            LastSentIsError = true;
            LastSentLabel = conn.LastError ?? conn.StatusMessage ?? "Connection failed";
        }
        else if (!string.IsNullOrEmpty(conn.LastError))
        {
            LastSentIsError = true;
            LastSentLabel = $"Failed {DateTime.Now:HH:mm:ss} \u2014 {conn.LastError}";
        }
        else if (conn.LastSentAt is { } sentAt)
        {
            LastSentIsError = false;
            LastSentLabel = $"Sent {sentAt:HH:mm:ss} \u2014 {conn.LastSentSummary}";
        }
    }

    private void SchedulePushPresence()
    {
        _presenceDebounce?.Cancel();
        _presenceDebounce = new CancellationTokenSource();
        var token = _presenceDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                Avalonia.Threading.Dispatcher.UIThread.Post(PushPresence);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void PushPresence()
    {
        var conn = EditorConnection;
        if (conn is null) return;
        var effective = BuildEffectivePreset();
        try
        {
            conn.UpdatePresence(effective);
            // UpdatePresence replaces conn.Preset with a new instance; keep
            // _connectedPreset in sync so ReferenceEquals still returns true.
            _connectedPreset = effective;
        }
        catch { /* swallow */ }
    }

    [RelayCommand]
    private void ToggleConnection()
    {
        if (IsConnected)
        {
            _connectedPreset = null;
            _services.Connections.Stop(ClientId.Trim());
            return;
        }

        if (LegacyRpcConflict.IsLegacyCustomRpRunning())
        {
            LastSentIsError = true;
            LastSentLabel = LegacyRpcConflict.WarningMessage;
            return;
        }

        // Validate category Client ID is configured.
        var category = Preset?.Metadata?.Category ?? "";
        if (!string.IsNullOrEmpty(category)
            && _services.Settings.Current.CategoryClientIds.TryGetValue(category, out var catId)
            && string.IsNullOrWhiteSpace(catId))
        {
            LastSentIsError = true;
            LastSentLabel = $"Set a Client ID for \u2018{category}\u2019 in Settings \u2192 Discord Applications, then click Save changes.";
            return;
        }

        _services.Settings.Current.LastClientId = EffectiveClientId;
        _services.Settings.Save();
        var effective = BuildEffectivePreset();
        _connectedPreset = effective;
        _services.Connections.Start(effective,
            string.IsNullOrWhiteSpace(PresetName) ? "Editor" : PresetName);
    }

    [RelayCommand]
    private void SendNow() => PushPresence();

    [RelayCommand]
    private async Task SaveAsAsync(Window? owner)
    {
        if (owner is null) return;
        var sp = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (sp is null) return;

        var preset = BuildPreset();
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save preset",
            SuggestedFileName = _services.Presets.SuggestFileName(preset),
            DefaultExtension = "crpreset",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CustomRP Preset") { Patterns = new[] { "*.crpreset" } },
            },
            SuggestedStartLocation = await sp.TryGetFolderFromPathAsync(_services.Presets.UserPresetsDirectory),
        });
        if (file is null) return;
        await _services.Presets.SaveAsync(preset, file.Path.LocalPath);
        IsDirty = false;
    }

    [RelayCommand]
    private async Task SaveToLibraryAsync()
    {
        var preset = BuildPreset();
        var filename = _services.Presets.SuggestFileName(preset);
        var target = Path.Combine(_services.Presets.UserPresetsDirectory, filename);
        await _services.Presets.SaveAsync(preset, target);
        IsDirty = false;
    }

    [RelayCommand]
    private async Task OpenAsync(Window? owner)
    {
        if (owner is null) return;
        var sp = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (sp is null) return;
        var picks = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open preset",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CustomRP Preset") { Patterns = new[] { "*.crpreset", "*.crp" } },
            },
        });
        var file = picks.FirstOrDefault();
        if (file is null) return;
        try
        {
            var loaded = await _services.Presets.LoadAsync(file.Path.LocalPath);
            LoadPreset(loaded);
        }
        catch { /* TODO surface a toast */ }
    }

    [RelayCommand]
    private void Reset() => LoadPreset(new Preset());
}
