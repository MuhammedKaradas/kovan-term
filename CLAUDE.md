# CLAUDE.md

Context and rules for Claude Code when working in this repo.

## Project

**Kovan Term**: a lightweight, Windows-only desktop app that runs several AI coding agents (Claude
Code, Codex, Gemini CLI…) side by side in real terminals on an infinite canvas. Low RAM usage and
smoothness compared to Electron-based alternatives (OpenCove, TermCanvas) are the main goals.

## Tech

- .NET 10, WPF (`net10.0-windows`)
- Canvas: [Nodify](https://github.com/miroiu/nodify)
- Terminal: [EasyWindowsTerminalControl](https://github.com/mitchcapper/EasyWindowsTerminalControl)
  (Windows Terminal's native control + ConPTY)

## Commands

```bash
dotnet build                        # build
dotnet run --project src/KovanTerm  # run
dotnet build -c Release             # use Release when measuring performance
dotnet test                         # unit tests (tests/KovanTerm.Tests)
```

### When running the app

- `KOVAN_DATA_DIR=<folder>`: separate folder for the workspace and profiles. **Always set it when
  testing so the user's `%APPDATA%\KovanTerm` data is never touched.**
- `KOVAN_PERF_SELFTEST=<log file>`: 8 s after startup, zooms/pans programmatically, writes frame times
  and snapshot-mode timings to the file, then closes the window. Run it before and after every change
  that can affect smoothness and put the results in the PR. Needs no mouse input.
- Don't send **real mouse/keyboard input** on the user's machine (SetCursorPos, mouse_event,
  SendInput): it goes to whatever window is in front. Only use UI Automation patterns (Invoke,
  SelectionItem) and window messages (`CloseMainWindow`).
- A test workspace node with a `profileId` gets its command rebuilt from the profile on startup; leave
  `profileId` out when a test needs a custom command line.

## Architecture notes (important)

- The terminal control is an **HwndHost**: it renders with DirectX in its own HWND. Therefore
  - it is **not scaled** by WPF `ScaleTransform`/zoom → the font size is changed with `SetTheme` on zoom;
  - WPF content **can't be drawn on top of it** (airspace). Keep this in mind when designing popups,
    tooltips etc. over terminals;
  - while panning/zooming/dragging, live terminals are hidden and replaced with snapshots taken by
    `TerminalSnapshot` ("snapshot mode"). Snapshots are cached in the background (only for terminals
    whose output changed). 150 ms after the movement ends, the live terminals come back. Measure with
    `KOVAN_PERF_SELFTEST` for any change that could affect this.
- Terminals outside the viewport sleep (HWND hidden, process keeps running).
- Details: `docs/architecture.md`; rationale for decisions: `docs/decisions/`.

## Rules

- **Commit messages**: Conventional Commits, in **English**. Details and examples in `CONTRIBUTING.md`.
  - **No attribution** in commits or PR descriptions: no `Co-Authored-By`, "Generated with Claude
    Code" or similar lines.
  - One line is enough for small changes; for changes that alter behavior or need justification,
    explain the *why* in the body.
- Code comments, user-facing text and docs are in English. Turkish lives only in `README.tr.md`.
- Follow the existing code style; `.editorconfig` applies.
- Check the license of any new dependency first (must be compatible: MIT/Apache/BSD).
- Put measurements (RAM, smoothness) in the PR description for performance-related changes.
- The roadmap lives in `docs/roadmap.md` and the GitHub milestones; close issues from the commit/PR
  (`Closes #12`).
