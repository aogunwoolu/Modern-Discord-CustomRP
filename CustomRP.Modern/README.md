# CustomRP Modern (v2)

Avalonia 11 + .NET 10 UI for CustomRP. Runs standalone — only needs `lib/DiscordRPC.dll` (vendored in this project).

The legacy WinForms app (`CustomRPC/`) is optional and kept in the solution for existing users.

## Build & run

```pwsh
dotnet build CustomRP.Modern/CustomRP.Modern.csproj
dotnet run --project CustomRP.Modern
```

## Preset library

- **Known Apps** — curated templates from `Assets/known-apps.json` (games, music, browsers, etc.).
- **Your Presets → Starter templates** — bundled `.crpreset` examples.
- **Your Presets → Saved presets** — files in `%APPDATA%/CustomRP.Modern/presets/` (create via editor **Save to library** or **Import**).

Legacy `.crp` (XML) files can be imported; they are converted to JSON on save.
