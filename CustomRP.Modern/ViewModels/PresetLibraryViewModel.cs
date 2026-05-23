using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomRP.Modern.Models;
using CustomRP.Modern.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CustomRP.Modern.ViewModels;

public partial class PresetLibraryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly PresenceEditorViewModel _editor;

    public const string AllCategoriesLabel = "All";

    public ObservableCollection<PresetEntry> BundledPresets { get; } = new();
    public ObservableCollection<PresetEntry> UserPresets { get; } = new();
    public ObservableCollection<KnownApp> KnownApps { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public int KnownAppCount => _services.KnownApps.Apps.Count;
    public string? KnownAppsLoadError => _services.KnownApps.LoadError;
    public bool HasBundledPresets => BundledPresets.Count > 0;
    public bool HasUserPresets => UserPresets.Count > 0;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedCategory = AllCategoriesLabel;
    [ObservableProperty] private KnownApp? _selectedKnownApp;
    [ObservableProperty] private bool _hasSelectedKnownApp;

    public PresetLibraryViewModel(AppServices services, PresenceEditorViewModel editor)
    {
        _services = services;
        _editor = editor;

        Categories.Add(AllCategoriesLabel);
        foreach (var cat in services.KnownApps.Categories)
            Categories.Add(cat);

        ApplyFilter();
        Refresh();
    }

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSelectedKnownAppChanged(KnownApp? value) => HasSelectedKnownApp = value is not null;

    [RelayCommand]
    public void Refresh()
    {
        BundledPresets.Clear();
        foreach (var entry in _services.Presets.ListBundledPresets())
            BundledPresets.Add(entry);

        UserPresets.Clear();
        foreach (var entry in _services.Presets.ListUserPresets())
            UserPresets.Add(entry);

        OnPropertyChanged(nameof(KnownAppCount));
        OnPropertyChanged(nameof(KnownAppsLoadError));
        OnPropertyChanged(nameof(HasBundledPresets));
        OnPropertyChanged(nameof(HasUserPresets));
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        SelectedCategory = category ?? AllCategoriesLabel;
    }

    [RelayCommand]
    private void SelectKnownApp(KnownApp? app) => SelectedKnownApp = app;

    [RelayCommand]
    private void OpenInEditor(KnownApp? app)
    {
        if (app is null) return;
        var preset = BuildFromKnownApp(app);
        _editor.LoadPreset(preset);
    }

    [RelayCommand]
    private void OpenScenarioInEditor(AppScenario? scenario)
    {
        if (scenario is null || SelectedKnownApp is not { } app) return;
        _editor.LoadPreset(BuildPresetFromScenario(app, scenario));
    }

    [RelayCommand]
    private void StartScenarioInBackground(AppScenario? scenario)
    {
        if (scenario is null || SelectedKnownApp is not { } app) return;
        if (string.IsNullOrWhiteSpace(app.ClientId)) return;
        _services.Connections.Start(
            BuildPresetFromScenario(app, scenario),
            $"{app.DisplayName} — {scenario.Name}");
    }

    [RelayCommand]
    private void StartKnownAppInBackground(KnownApp? app)
    {
        if (app is null || string.IsNullOrWhiteSpace(app.ClientId)) return;
        _services.Connections.Start(BuildFromKnownApp(app), app.DisplayName);
    }

    private Preset BuildPresetFromScenario(KnownApp app, AppScenario scenario)
    {
        var preset = BuildFromKnownApp(app);
        if (!string.IsNullOrWhiteSpace(scenario.Details)) preset.Details = scenario.Details;
        if (!string.IsNullOrWhiteSpace(scenario.State)) preset.State = scenario.State;
        if (!string.IsNullOrWhiteSpace(scenario.LargeImageKey))
            preset.LargeImage = new ImageAsset { Key = scenario.LargeImageKey };
        if (!string.IsNullOrWhiteSpace(scenario.SmallImageKey))
            preset.SmallImage = new ImageAsset { Key = scenario.SmallImageKey };
        preset.Metadata.Name = $"{app.DisplayName} — {scenario.Name}";
        preset.Metadata.Description = scenario.Description;
        return preset;
    }

    [RelayCommand]
    private void Load(PresetEntry? entry)
    {
        if (entry is null) return;
        _editor.LoadPreset(entry.Preset);
    }

    [RelayCommand]
    private void StartUserPresetInBackground(PresetEntry? entry)
    {
        if (entry?.Preset is not { } preset) return;
        if (string.IsNullOrWhiteSpace(preset.ClientId)) return;
        _services.Connections.Start(preset, preset.Metadata.Name);
    }

    [RelayCommand]
    private void Delete(PresetEntry? entry)
    {
        if (entry is null || entry.IsBundled) return;
        try { File.Delete(entry.FilePath); } catch { /* ignored */ }
        Refresh();
    }

    [RelayCommand]
    private async Task SaveBundledCopyAsync(PresetEntry? entry)
    {
        if (entry is not { IsBundled: true }) return;
        var target = Path.Combine(
            _services.Presets.UserPresetsDirectory,
            _services.Presets.SuggestFileName(entry.Preset));
        await _services.Presets.SaveAsync(entry.Preset, target);
        Refresh();
    }

    [RelayCommand]
    private async Task ImportAsync(Window? owner)
    {
        if (owner is null) return;
        var sp = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (sp is null) return;

        var picks = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import preset(s)",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CustomRP Preset") { Patterns = new[] { "*.crpreset", "*.crp" } },
            },
        });
        foreach (var file in picks)
        {
            try
            {
                var preset = await _services.Presets.LoadAsync(file.Path.LocalPath);
                var target = Path.Combine(_services.Presets.UserPresetsDirectory,
                    _services.Presets.SuggestFileName(preset));
                await _services.Presets.SaveAsync(preset, target);
            }
            catch { /* skip bad files */ }
        }
        Refresh();
    }

    [RelayCommand]
    private async Task ExportAsync(PresetEntry? entry)
    {
        if (entry is null) return;
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.MainWindow;
        if (owner is null) return;
        var sp = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (sp is null) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export preset",
            SuggestedFileName = _services.Presets.SuggestFileName(entry.Preset),
            DefaultExtension = "crpreset",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CustomRP Preset") { Patterns = new[] { "*.crpreset" } },
            },
        });
        if (file is null) return;
        await _services.Presets.SaveAsync(entry.Preset, file.Path.LocalPath);
    }

    private Preset BuildFromKnownApp(KnownApp app)
    {
        // Use the app's resolved icon as the large image when no explicit key is configured.
        var largeKey = !string.IsNullOrWhiteSpace(app.DefaultLargeImageKey)
            ? app.DefaultLargeImageKey
            : app.IconUrl ?? "";

        var preset = new Preset
        {
            ClientId = app.ClientId ?? "",
            ActivityName = app.DisplayName,
            Type = app.DefaultActivityType,
            Details = app.DefaultDetails,
            State = app.DefaultState,
            Timestamps = app.DefaultTimestamps,
            LargeImage = new ImageAsset
            {
                Key = largeKey,
                Text = app.DefaultLargeImageText,
            },
            SmallImage = new ImageAsset
            {
                Key = app.DefaultSmallImageKey,
                Text = app.DefaultSmallImageText,
            },
            Metadata = new PresetMetadata
            {
                Name = app.DisplayName,
                Description = app.Description,
                Tags = new List<string>(app.Tags),
                IconUrl = app.IconUrl ?? "",
                Category = app.Category,
            },
        };
        if (app.DefaultAutoUpdate is { } au)
        {
            preset.AutoUpdate = new AutoUpdateConfig
            {
                Enabled = au.Enabled,
                Strategy = au.Strategy,
                ProcessName = au.ProcessName,
                IntervalSeconds = au.IntervalSeconds,
                DetailsTemplate = au.DetailsTemplate,
                StateTemplate = au.StateTemplate,
                UseFaviconAsSmallImage = au.UseFaviconAsSmallImage,
            };
        }
        return preset;
    }

    private void ApplyFilter()
    {
        KnownApps.Clear();
        var query = (Search ?? "").Trim();
        IEnumerable<KnownApp> src = _services.KnownApps.Apps;

        if (!string.Equals(SelectedCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase))
            src = src.Where(a => string.Equals(a.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(query))
            src = src.Where(a =>
                a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Subcategory.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                a.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var app in src.OrderBy(a => a.Category).ThenBy(a => a.DisplayName))
            KnownApps.Add(app);
    }
}
