# Kovan Term

**English** · [Türkçe](README.tr.md)

A lightweight Windows app that runs AI coding agents (Claude Code, Codex, Gemini CLI…) side by side on
an **infinite canvas**, each in its own **real terminal**.

> ⚠️ Early development (v0.1). Roadmap: [docs/roadmap.md](docs/roadmap.md)

## Why?

Juggling several agents across terminal tabs is hard. Electron-based canvas apps (OpenCove, TermCanvas)
solve this but are heavy. Kovan Term goes after the same idea with native .NET/WPF and Windows
Terminal's GPU-accelerated terminal control, keeping resource usage low: 4 terminals use ~190 MB of RAM,
about 3.5× less than an xterm.js-in-WebView2 equivalent
([measurement](docs/decisions/0002-terminal-dependency-risk.md)).

## Features

- Infinite canvas you can pan and zoom
- Real terminals as draggable, resizable nodes on the canvas
- Profiles: PowerShell, cmd, Claude Code, Codex, Gemini CLI, plus your own ([docs/profiles.md](docs/profiles.md))
- WSL: a shell and Claude Code for each installed distro, started in a Linux directory
- Pick a working directory; right-click the canvas for "New terminal here"
- Windows notifications when an agent needs you or finishes while you're in another app; click to go to it
- Several workspaces (e.g. one per project) as tabs; agents on hidden ones keep running and show when they wait
- The workspace is saved automatically and restored on startup
- Dictation: speak to type into a terminal, transcribed on your computer with Whisper (`Ctrl+Shift+Space`)
- Keyboard shortcuts: switch between terminals, fit all, zoom to a terminal ([docs/shortcuts.md](docs/shortcuts.md))
- Smooth panning and zooming with "snapshot mode"; off-screen terminals sleep
- Windows 11 look (Mica, Fluent controls) with light, dark or follow-Windows themes

## Requirements

- Windows 10 1903+ / Windows 11
- The CLIs for the agents you want to use (e.g. `claude`) on your `PATH`

## Install

Download `KovanTerm-<version>-win-x64.zip` from the
[Releases](https://github.com/MuhammedKaradas/kovan-term/releases) page, extract it to a folder and run
`KovanTerm.exe`. No .NET installation needed. The executable isn't signed yet, so if SmartScreen warns
you, choose "More info → Run anyway".

## Run from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/MuhammedKaradas/kovan-term.git
cd kovan-term
dotnet run --project src/KovanTerm -c Release
```

## Documentation

- [Architecture](docs/architecture.md)
- [Profiles](docs/profiles.md)
- [Keyboard shortcuts](docs/shortcuts.md)
- [Roadmap](docs/roadmap.md)
- [Architecture decision records](docs/decisions/)
- [Contributing](CONTRIBUTING.md)

## License

[MIT](LICENSE)
