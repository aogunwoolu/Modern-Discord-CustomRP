# CustomRP

**A modern Discord Rich Presence manager.** Set custom statuses, auto-detect running apps, live-update presence from browser URLs or window titles, and run multiple presences simultaneously — one per app category.

Built with [Avalonia 11](https://avaloniaui.net/) and [Lachee's DiscordRPC for C#](https://github.com/Lachee/discord-rpc-csharp).

---

## Features

- **Preset library** — hundreds of pre-configured known apps (games, browsers, music players, IDEs, and more). One click to load and connect.
- **Auto-detect** — scans your running processes and matches them against the known-app library. Hit *Use* and the editor is pre-filled instantly.
- **Live auto-update** — polls a process at a configurable interval. Pulls the current window title or browser URL and pushes it to Discord in real time.
- **Template fields** — use `{title}`, `{url}`, `{host}`, `{path}`, `{scheme}`, `{query}`, `{port}`, or `{process}` in any text field to inject live data. Both `{var}` and `{{var}}` syntax work.
- **Favicon as small image** — when browsing, the current site's icon is automatically used as the small image in your presence.
- **Multiple simultaneous presences** — each app category (Games, Browsers, Music, etc.) runs through its own Discord Application ID, so they stack in Discord's Activity Shelf without replacing each other.
- **Active Presences panel** — live view of every running connection with status, username, and last-sent summary. Reconnect or stop individual connections at any time.
- **Preset save/load** — save any editor state as a `.crpreset` file. Import legacy `.crp` (XML) files from older CustomRP versions.

---

## Getting Started

### Requirements

- Windows 10 or later
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (desktop apps)
- Discord running on the same machine

### Download

Grab the latest build from the [Releases](https://github.com/maximmax42/Discord-CustomRP/releases) page.

### First run

1. Open the app. The **Preset Library** opens by default — pick any known app or start fresh in the **Editor**.
2. In the Editor, a Discord Application ID is pre-filled per category. You can connect immediately with the defaults.
3. Click **Connect**. Your presence goes live.

> **Multiple presences at once?** See [Multiple Presences](#multiple-presences) below.

---

## Multiple Presences

Discord stacks activities from different Application IDs in your **Activity Shelf** (visible when someone clicks your profile). CustomRP assigns each app category its own ID so they never overwrite each other.

| Category | Default app |
|---|---|
| 🌐 Browsers | Separate ID |
| 💬 Communication | Separate ID |
| 🎨 Creative | Separate ID |
| 💻 Development | Separate ID |
| 🎮 Games | Separate ID |
| 🚀 Launchers | Separate ID |
| 🎵 Music | Separate ID |
| 📺 Video | Separate ID |
| 📋 Productivity | Shared fallback |

To use your **own** Application IDs (recommended for forks/self-hosting):

1. Go to **[discord.com/developers/applications](https://discord.com/developers/applications)**.
2. Create a new application for each category you want — name it anything (e.g. *"CustomRP – Games"*).
3. Copy the **Application ID** from the General Information page.
4. In CustomRP, open **Settings → Discord Applications** and paste each ID into the matching row.
5. Click **Save changes**.

You can create up to 25 free applications. Categories sharing the same ID will replace each other; categories with unique IDs run side by side.

---

## Auto-Update & Templates

Enable **Auto-update** in the editor for any presence and pick a strategy:

| Strategy | What it reads |
|---|---|
| **Window Title** | The active window's title bar text |
| **Browser URL** | The address bar of Chrome, Edge, Firefox, or Arc (via UI Automation) |

### Template variables

Use these in **Details**, **State**, or **Button** fields:

| Variable | Value |
|---|---|
| `{title}` | Window / tab title |
| `{url}` | Full URL (browser strategy) |
| `{host}` | Domain only — e.g. `github.com` |
| `{path}` | URL path — e.g. `/user/repo` |
| `{scheme}` | Protocol — e.g. `https` |
| `{query}` | Query string (without `?`) |
| `{port}` | Port number (empty if default) |
| `{process}` | Process name |

Both `{var}` and `{{var}}` are accepted. Values longer than 128 characters are trimmed automatically.

---

## Building

Requires .NET 10 SDK.

```pwsh
git clone https://github.com/maximmax42/Discord-CustomRP
cd Discord-CustomRP
dotnet build CustomRP.Modern/CustomRP.Modern.csproj
dotnet run --project CustomRP.Modern
```

The legacy WinForms app (`CustomRPC/`) is kept in the solution for Windows 7/8 users and is built separately.

---

## Troubleshooting

**Presence not showing?**
Check that Discord is running, then look at **Settings → Diagnostics** for the RPC log path. Open the log to see connection events.

**Only one presence showing in Discord?**
Discord shows multiple activities in the **Activity Shelf** — click your own avatar in Discord to see all of them. The bottom-left status bar only shows the primary activity.

**Another app overriding my presence?**
Discord auto-detects some running apps. Go to **Discord Settings → Activity Privacy → Activity Settings** and disable detection for that app, or set its integration to off within the app itself.

---

## Privacy

See [PRIVACY.md](PRIVACY.md).

