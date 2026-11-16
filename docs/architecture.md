# Architecture

## Overview

The app is written with MVVM ([CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/))
and uses [WPF-UI](https://github.com/lepoco/wpfui) for the Windows 11 look (Mica backdrop, Fluent controls,
light/dark themes).
State and commands live in view models; anything specific to `HwndHost` stays in the view layer.

```
MainWindow  (DataContext: MainViewModel)
 └─ NodifyEditor (infinite canvas; ViewportZoom/ViewportLocation ↔ MainViewModel)
     └─ ItemContainer (one per node; Location ↔ TerminalNodeViewModel)
         └─ TerminalNodeViewModel template
             ├─ Header (status dot, title, close → CloseNodeCommand)
             └─ Grid
                 ├─ Image                (snapshot shown in snapshot mode)
                 └─ EasyTerminalControl  (HwndHost → Windows Terminal control → ConPTY → process)
```

```
src/KovanTerm/
├─ Models/        Plain data: NodeStatus, TerminalProfile, WorkspaceState, AppSettings
├─ ViewModels/    MainViewModel, TerminalNodeViewModel, NewTerminalViewModel, NodePlacement, NodeNavigation
├─ Views/         MainWindow (+ .Shortcuts), NewTerminalWindow, DialogService, TerminalCanvasController,
│                 ThemeService, TerminalThemes, Shortcuts, PerfSelfTest, Converters/
├─ Services/      WorkspaceStore, SettingsStore, ProfileStore, CommandResolver, AppPaths, IDialogService
└─ Interop/       Win32 P/Invoke (Native) and HWND capture (TerminalSnapshot)
tests/KovanTerm.Tests/   xUnit unit tests
```

| File | Responsibility |
|---|---|
| `ViewModels/MainViewModel.cs` | Node list, viewport state, commands (`NewTerminal`, `CloseNode`), persistence |
| `ViewModels/NewTerminalViewModel.cs` | "New terminal" dialog: profile and working directory selection, validation |
| `ViewModels/NodePlacement.cs` | Finds a free, preferably visible spot for a new node |
| `ViewModels/NodeNavigation.cs` | Reading order for keyboard navigation between terminals |
| `ViewModels/TerminalNodeViewModel.cs` | Node: title, command, size, location, status |
| `Services/CommandResolver.cs` | Resolves programs via `PATH`/`PATHEXT`, builds the ConPTY command line |
| `Services/ProfileStore.cs` | Built-in profiles + `profiles.json` ([profiles.md](profiles.md)) |
| `Services/AppPaths.cs` | Data folder (`%APPDATA%\KovanTerm`, overridable with `KOVAN_DATA_DIR`) |
| `Views/MainWindow.xaml(.cs)` | Canvas and node template; code-behind only registers terminals with the controller |
| `Views/MainWindow.Shortcuts.cs` | Keyboard shortcuts ([shortcuts.md](shortcuts.md)) |
| `Views/TerminalCanvasController.cs` | Snapshot mode, snapshot cache, sleeping, zoom → font size, process cleanup |
| `Views/ThemeService.cs` | Applies the light/dark theme (WPF-UI + Mica) and tells the terminal layer which one is active |
| `Views/TerminalThemes.cs` | Terminal color schemes for dark and light |
| `Views/PerfSelfTest.cs` | Reproducible smoothness benchmark (`KOVAN_PERF_SELFTEST`) |
| `Interop/TerminalSnapshot.cs` | Captures a terminal HWND |

Rule: view models never touch WPF controls (`EasyTerminalControl`, `NodifyEditor`). Code that must
work with the terminal control directly lives under `Views/`. Windows a view model needs to open go
through `IDialogService` (implemented by `DialogService` in the view layer), so view models stay testable.

The viewport (zoom/location) only changes through the user or explicit commands. Nodify's automatic
panning to a focused node (`NodifyEditor.AutoPanOnNodeFocus`) is disabled: terminals take focus on
startup and on click, which used to move the saved viewport.

New nodes are placed so they never overlap another node (`NodePlacement`): because of airspace,
overlapping terminals would cover each other's headers.

## Terminal layer and its constraints

`EasyTerminalControl` hosts Windows Terminal's native C++/DirectX control inside an **HwndHost**.
Processes are started through ConPTY. Consequences:

1. **No scaling**: an HwndHost ignores WPF's `RenderTransform`; when Nodify zooms, the HWND shrinks but
   the text doesn't. Fix: when the zoom changes, set the font size to `13 × zoom` via
   `Terminal.SetTheme(..., fontSize)`.
2. **Airspace**: WPF content can't be drawn on top of a terminal. Overlapping nodes, popups over
   terminals, etc. are limited because of this.
3. **Cost of movement**: on every pan/zoom frame all HWNDs are repositioned and resized, which stutters.
   Fix: "snapshot mode":
   - On `ViewportUpdated` or `ItemContainer.LocationChanged`, `Freeze()` hides the terminals and shows
     their snapshots in an `Image` instead.
   - 150 ms after the last movement, `Unfreeze()` updates the font size and brings the terminals back;
     snapshots stay underneath for another 100 ms until the terminal draws its first frame (no blank flash).
   - For interactions of unknown length, such as resizing, `BeginInteraction()`/`EndInteraction()`
     keep snapshot mode on until the interaction ends; the snapshot stretches to the new size and the
     terminal redraws at the real size on release.
   - **Snapshot cache:** `PrintWindow(PW_RENDERFULLCONTENT)` waits for a DWM frame (~15 ms). Capturing
     at the start of a gesture froze the UI for ~100-170 ms with 10 terminals. Instead, the terminal's
     `TerminalOutput` event and size changes set a "dirty" flag, and every 150 ms the snapshots of
     changed terminals are refreshed **on a background thread**. `Freeze()` uses the cached snapshots
     and only captures stale ones on the spot.
   - **How snapshots are captured:** the top-level window is captured once and each terminal control
     (HWND plus its WPF scrollbar) is cropped out of it; the part outside the canvas is painted with the
     terminal background. Capturing each terminal HWND directly was unreliable once a terminal was
     clipped: depending on timing, `PrintWindow` returned the visible part either in place or aligned
     to the top-left corner. Cropping the whole control (not just the HWND) also keeps the snapshot
     from being stretched over the scrollbar. Live and frozen screenshots of clipped terminals are
     pixel-identical.
   - Benchmark: `KOVAN_PERF_SELFTEST` (see CLAUDE.md). ~58 fps with 10 terminals, worst frame ~35 ms
     (~200 ms before the cache).
   - Known limitation: the font size is an integer (`SetTheme` takes a `short`), so after a gesture
     the terminal text can come back at a slightly different size than the scaled snapshot (a small
     "jump", #34).
4. **Sleeping terminals**: terminals outside the visible area have their HWND hidden (on settle,
   `UpdateSleepStates`). The process keeps running and output keeps going into the terminal buffer;
   on wake, the current state is drawn. Below zoom 0.4 (text ≤5 pt, unreadable) every terminal that
   has a snapshot sleeps and shows its last snapshot.
   - Measured (20 terminals all producing output continuously, 6 visible): GPU 7.8% → 3.2%. CPU
     didn't change (~3 cores): almost all of it is VT parsing, which has to happen even while hidden.
5. **Resize grip**: since WPF can't draw over a terminal, the grip sits in a thin strip below it.
   `Thumb.DragDelta` values are measured inside the node, so they're already in canvas units.
6. **Clipping to the canvas**: an HWND ignores WPF clipping, so a terminal panned past the canvas edge
   would draw over the toolbar (#41). Each awake terminal gets a window region (`SetWindowRgn`, see
   `TerminalClip`) equal to its intersection with the canvas, updated after the viewport settles.
7. **Shortcuts while a terminal has focus**: keys go straight to the terminal's HWND and never reach
   WPF `KeyBinding`s. The terminal HWND lives on the UI thread, so key messages are caught in
   `ComponentDispatcher.ThreadPreprocessMessage` before the terminal sees them.

## Theming

`ThemePreference` (system / dark / light) lives in the view model and is saved in `settings.json`; the
toolbar button cycles through it. `ThemeService` applies it with WPF-UI's `ApplicationThemeManager`
(following Windows via `SystemThemeWatcher` for "system") and raises `IsDarkChanged`, which
`TerminalCanvasController` uses to switch terminal color schemes, since terminal colors aren't WPF
resources. UI colors come from WPF-UI's theme resources (`DynamicResource`), never hard-coded.

- The canvas is transparent so the Mica backdrop shows through; terminals stay opaque.
- Light scheme caveat: legacy console apps (cmd, Windows PowerShell) paint "default" text with explicit
  palette entries (background 0, foreground 7), so the light palette maps entry 0 to the light
  background and entry 7 to the dark foreground.
- Nodify's own square item border is made transparent; the node's rounded border shows selection with
  the accent color instead.
- Cost: WPF-UI adds ~19 MB working set (4 terminals: 198 → 217 MB); smoothness is unchanged.

## Persistence

The workspace is stored in `%APPDATA%\KovanTerm\workspace.json` (`WorkspaceStore`); app-wide
preferences such as the theme in `settings.json` (`SettingsStore`).

- **What's saved:** nodes (id, profile, title, command, color, working directory, location, size),
  the viewport (zoom, location), recent directories and the last used profile. Terminal contents and
  agent state aren't saved; on startup terminals restart with the same command.
- **When:** 500 ms after a node is added/closed/moved/resized or the viewport changes, and on window close.
- **Safety:** written to a `.tmp` file first and then swapped in (no half-written files). A file that
  can't be read, or was written by a newer version, is backed up as `workspace.corrupt-<timestamp>.json`
  and the app starts with the default workspace.
- **Versioning:** `WorkspaceState.Version`. Adding fields is backward compatible; changing the meaning
  of a field requires bumping the version and writing a migration.

## Dependency risk

`EasyWindowsTerminalControl` relies on a community repackage (`CI.Microsoft.Terminal.Wpf`) of the WPF
terminal control that Microsoft doesn't publish officially. Risks, a memory comparison with the
WebView2 + xterm.js fallback, and mitigations: [decision record 0002](decisions/0002-terminal-dependency-risk.md).
In short: package versions are pinned and updates are tested by hand.

## Agent status

Claude Code nodes are started with `--settings <data dir>\hooks\<node id>.json`, whose HTTP hooks post to
`AgentHookServer` (127.0.0.1, random port, per-run token). `AgentStatusReducer` turns the events into the
node's status; Allow/Deny type into the terminal. Details and alternatives: [0003](decisions/0003-agent-status-via-http-hooks.md).

Each Claude Code node keeps its conversation: a new node is started with `--session-id <uuid>` (saved as
`SessionId` in the workspace), later launches use `--resume <uuid>` once Claude has saved the transcript
(`ClaudeSessions`). Hook events carry the current id, so `/clear` moves the node to the new conversation.
A profile that passes its own `--resume`, `--continue` or `--session-id` is left alone.

**Recent** (`Ctrl+Shift+O`) lists saved Claude Code conversations (`ClaudeConversations`): the 300 most recently written
transcripts in `<claude config dir>\projects\*\*.jsonl`, reading only each file's first 128 KB (folder, first prompt)
and last 64 KB (latest `custom-title` / `ai-title`), since transcripts add up to hundreds of MB. Picking one opens a
node in its folder with that session id, so it's resumed; one already on the canvas is gone to instead.

Programs that write OSC 7501 status reports without waiting for the terminal's answer (Codex) are read from
the terminal output by `ProgramStatusParser`; Allow types the program's own "Yes" key (`y` for Codex).

## Agents panel

A list of every node beside the canvas (left), grouped by working folder, with the agents waiting for the user first
(`ListCollectionView` with live sorting on `StatusOrder`). Each row shows the status, the context the conversation
uses (`TranscriptUsage` reads only what was appended to the transcript, off the UI thread, on `Stop`/`PostToolUse`
hooks) and running subagents (`SubagentStart`/`SubagentStop`). It updates only on status events.

The panel is square and flush with the window edge: a large rounded, bordered panel at this spot dropped canvas
motion from ~59 to ~34 fps. Keep large static chrome next to the canvas free of `CornerRadius`, and measure.

## Screen rules

For prompts no hook or status report announces (folder trust questions, Codex's hook review) and for agents that
report nothing (Gemini CLI), the bottom of the terminal screen is matched against data-driven rules
(`Resources/screen-rules.json`, overridable per id in `<data dir>\screen-rules.json`): highest priority wins, no match
means no opinion, phrases match across line breaks. The visible text is read through the terminal control's UI
Automation text pattern (`TerminalScreenReader`, off the UI thread; the control has no other way to read its buffer),
only for agents with rules and only after output changed: once it settles for 0.4 s (a prompt waits for the user, so
its output stops), at most every 1.5 s, and every 3 s while output keeps flowing. Sleeping terminals are read too.
The agent's own reports win; the screen only adds a "waiting" they didn't report, or is the only source for agents
that report nothing. Captured screens in `tests/KovanTerm.Tests/ScreenFixtures` keep the rules honest.

## Changes panel

Right of the canvas, for the current terminal (last clicked, navigated to, or the one in the middle at startup):
`git status` and the selected file's diff against `HEAD` (untracked files: against an empty file), through
`GitChanges`, which runs the `git` command line read-only (`GIT_OPTIONAL_LOCKS=0`, so it doesn't fight the agent's
own git commands for the index lock; output capped at 2 MB). It refreshes 700 ms after the agent's `PostToolUse` /
`Stop` hook or a `done`/`idle` status report, and on demand. The diff list is virtualized.

Notes left on diff lines are pasted into the terminal as one bracketed paste (`ESC [200~ … ESC [201~`): the agent's
prompt takes it as a single block and nothing is sent until the user presses Enter.

## Worktrees

"Start in a new git worktree" in the New terminal dialog (offered when the folder is inside a git repository)
creates a branch from the current `HEAD` and checks it out in `<repo>.worktrees\<branch>` next to the repository
(`GitWorktrees`); the node opens there and saves the worktree (`NodeState.Worktree`: repository, path, branch and the
commit it started from). The Changes panel compares such a node with that commit, so the agent's commits show too.
Closing the node asks whether to keep the worktree or delete it and its branch, saying how many uncommitted files
and commits would be lost. Removal retries briefly, since the node's shell may still be releasing the folder.

The Changes panel header of a worktree node has **Merge into <base>** (in the main checkout; refused when it's on
another branch or has uncommitted changes, conflicts abort the merge so the base branch is never left half-merged)
and **Pull request** (push to `origin`, then open the GitHub/GitLab "new pull request" page). A **setup command**
per repository (stored in `settings.json`) runs in a new worktree before the agent's first start
(`cmd /s /k "set KOVAN_REPO=… && <setup> && <agent>"`; `%KOVAN_REPO%` is substituted in the command itself, since cmd
expands variables before `set` runs). A node whose folder is gone isn't started: it would start in Kovan's own folder.

## Planned components

- **Voice** (v0.4): local speech recognition with Whisper.net.
