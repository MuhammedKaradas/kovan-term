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
- Profiles: PowerShell, cmd, Claude Code, Codex, Gemini CLI, Cursor CLI, Antigravity, plus your own ([docs/profiles.md](docs/profiles.md))
- WSL: a shell, Claude Code, Codex and Gemini CLI for each installed distro, started in a Linux directory
- Pick a working directory; right-click the canvas for "New terminal here"
- Send one prompt to several agents at once, e.g. to compare Claude Code, Codex and Gemini CLI on the same task
- Windows notifications when an agent needs you or finishes while you're in another app; click to go to it
- Drop files or screenshots on a terminal to give their paths to the agent
- Statistics: time and tokens by day, project and model, from Claude Code's and Codex's own session files
- Several workspaces (e.g. one per project) as tabs; agents on hidden ones keep running and show when they wait
- The workspace is saved automatically and restored on startup
- Dictation: speak to type into a terminal, transcribed on your computer with Whisper (`Ctrl+Shift+Space`)
- Voice commands in Turkish and English: "go to the waiting agent", "tell Claude to run the tests" (`Ctrl+Shift+K`)
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
- [Contributing](CONTRIBUTING.md): how to build, test and send a change
- [Code of conduct](CODE_OF_CONDUCT.md)
- [Security policy](SECURITY.md): report vulnerabilities privately

## Acknowledgements

Kovan Term began as a lightweight, Windows-native take on canvas-based agent apps. As it grew, it learned a great
deal from **[omux](https://github.com/onurozcan/omux)** by Onur Özcan ([onurozcan](https://github.com/onurozcan)): a native macOS
app (SwiftUI) that runs Claude Code and Codex sessions side by side, each on its own branch. Thank you for building it
in the open.

On a Mac you can use omux itself. On Linux, [omux for Linux](https://github.com/radioheavy/omux-linux) by
İsmail Oktay Dak ([radioheavy](https://github.com/radioheavy)) brings the same app there (GTK 4, Rust). Kovan Term isn't a port: it's
a canvas of its own for Windows that learned from omux.

Many of Kovan Term's features follow omux's lead, among them:
- reading Codex's status from its OSC 7501 reports, and answering permission prompts from outside the terminal;
- the Changes panel with notes sent to the agent;
- a worktree's pull request status, and its failed checks sent to the agent;
- notifications, the status light and keeping the computer awake;
- naming terminals after their conversation, and dropping files on a terminal;
- statistics of time and tokens.

The code is Kovan Term's own (C#/WPF), but how several of these work was learned from omux's source (MIT).

Where it goes its own way:
- an infinite canvas with notes, groups, web previews and workspaces;
- dictation and voice commands;
- an agent mesh with messages, a task board and file leases;
- agents inside WSL;
- one prompt sent to several agents.

## License

[MIT](LICENSE)
