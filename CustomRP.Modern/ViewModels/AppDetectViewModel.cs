using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomRP.Modern.Models;
using CustomRP.Modern.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace CustomRP.Modern.ViewModels;

public partial class AppDetectViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly PresenceEditorViewModel _editor;
    private readonly Action _goToEditor;

    public ObservableCollection<DetectedApp> Detected { get; } = new();

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _lastScanLabel = "Not scanned yet";
    [ObservableProperty] private DetectedApp? _selected;

    public AppDetectViewModel(AppServices services, PresenceEditorViewModel editor, Action goToEditor)
    {
        _services = services;
        _editor = editor;
        _goToEditor = goToEditor;
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            var results = await _services.Detection.ScanAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Detected.Clear();
                foreach (var app in results) Detected.Add(app);
                LastScanLabel = $"{Detected.Count} app(s) found — {DateTime.Now:T}";
            });
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void UseDetected(DetectedApp? app)
    {
        if (app is null) return;
        var iconUrl = app.Match?.IconUrl ?? "";
        var preset = new Preset
        {
            ClientId = app.Match?.ClientId ?? "",
            ActivityName = app.Match?.DisplayName ?? app.ProcessName,
            Details = app.WindowTitle,
            LargeImage = new ImageAsset { Key = iconUrl },
            Metadata = new PresetMetadata
            {
                Name = (app.Match?.DisplayName ?? app.ProcessName) + " preset",
                Description = app.Match?.Notes ?? $"Auto-detected from running process '{app.ProcessName}'",
                Tags = new System.Collections.Generic.List<string> { "auto-detected" },
                IconUrl = iconUrl,
                Category = app.Match?.Category ?? "",
            },
        };
        _editor.LoadPreset(preset);
        _goToEditor();
    }
}
