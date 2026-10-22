# Architecture

## Overview

The app is written with MVVM ([CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)).
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
├─ Models/        Plain data: NodeStatus, TerminalProfile, WorkspaceState
├─ ViewModels/    MainViewModel, TerminalNodeViewModel, NewTerminalViewModel, NodePlacement, NodeNavigation
├─ Views/         MainWindow (+ .Shortcuts), NewTerminalWindow, DialogService, TerminalCanvasController,
│                 Shortcuts, PerfSelfTest, Converters/
├─ Services/      WorkspaceStore, ProfileStore, CommandResolver, AppPaths, IDialogService
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
   - **Snapshot cache:** `PrintWindow(PW_RENDERFULLCONTENT)` waits for a DWM frame, so it takes ~12-20 ms
     per terminal, and calls can't be parallelized (they serialize). Capturing at the start of a
     gesture froze the UI for ~100-170 ms with 10 terminals. Instead, the terminal's `TerminalOutput`
     event and size changes set a "dirty" flag, and every 150 ms the snapshots of changed terminals are
     captured **on a background thread**. `Freeze()` uses the cached snapshot (<1 ms) and only captures
     stale ones on the spot.
   - **Clipped terminals:** for an HWND that extends past the window, `PrintWindow` draws only the
     visible part, aligned to the top-left corner. `TerminalSnapshot` shifts the visible part to its
     real position and paints the clipped part with the terminal background.
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
6. **Shortcuts while a terminal has focus**: keys go straight to the terminal's HWND and never reach
   WPF `KeyBinding`s. The terminal HWND lives on the UI thread, so key messages are caught in
   `ComponentDispatcher.ThreadPreprocessMessage` before the terminal sees them.

## Persistence

The workspace is stored in `%APPDATA%\KovanTerm\workspace.json` (`WorkspaceStore`).

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

## Planned components

- **Agent status bridge** (v0.2): Claude Code hooks → local named pipe → node status.
- **Voice** (v0.4): local speech recognition with Whisper.net.
