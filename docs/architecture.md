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
                 └─ TerminalHost         (ITerminalSurface: HwndHost → Windows Terminal control → ConPTY → process)
```

```
src/KovanTerm/
├─ Models/        Plain data: NodeStatus, TerminalProfile, WorkspaceState, AppSettings
├─ ViewModels/    MainViewModel, TerminalNodeViewModel, NewTerminalViewModel, NodePlacement, NodeNavigation
├─ Terminals/     ITerminalSurface, TerminalHost; WindowsTerminal/ (the one implementation)
├─ Views/         MainWindow (+ .Shortcuts), NewTerminalWindow, DialogService, TerminalCanvasController,
│                 ThemeService, TerminalThemes, Shortcuts, PerfSelfTest, Converters/
├─ Services/      WorkspaceStore, SettingsStore, ProfileStore, CommandResolver, WslLauncher, AppPaths, IDialogService
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
| `Services/WslLauncher.cs` | Lists WSL distros, builds the `wsl.exe` command line, converts `\\wsl.localhost` paths, waits for a distro to finish booting before its terminals start, runs persistent nodes in tmux sessions |
| `Services/AppPaths.cs` | Data folder (`%APPDATA%\KovanTerm`, overridable with `KOVAN_DATA_DIR`) |
| `Views/MainWindow.xaml(.cs)` | Canvas and node template; code-behind only registers terminals with the controller |
| `Views/MainWindow.Shortcuts.cs` | Keyboard shortcuts ([shortcuts.md](shortcuts.md)) |
| `Views/TerminalCanvasController.cs` | Snapshot mode, snapshot cache, sleeping, zoom → font size, process cleanup |
| `Views/ThemeService.cs` | Applies the light/dark theme (WPF-UI + Mica) and tells the terminal layer which one is active |
| `Views/TerminalThemes.cs` | Terminal color schemes for dark and light |
| `Views/PerfSelfTest.cs` | Reproducible smoothness benchmark (`KOVAN_PERF_SELFTEST`) |
| `Interop/TerminalSnapshot.cs` | Captures a terminal HWND |

Rule: view models never touch WPF controls (the terminal, `NodifyEditor`). Code that must work with
the terminal directly lives under `Views/` and only knows `Terminals/ITerminalSurface`; the package's
types (`EasyTerminalControl`, `Microsoft.Terminal.Wpf`) appear only in `Terminals/WindowsTerminal/`.
Windows a view model needs to open go through `IDialogService` (implemented by `DialogService` in the
view layer), so view models stay testable.

The viewport (zoom/location) only changes through the user or explicit commands. Nodify's automatic
panning to a focused node (`NodifyEditor.AutoPanOnNodeFocus`) is disabled: terminals take focus on
startup and on click, which used to move the saved viewport.

New nodes are placed so they never overlap another node (`NodePlacement`): because of airspace,
overlapping terminals would cover each other's headers.

## Terminal layer and its constraints

The terminal is an `ITerminalSurface` (`Terminals/`): start a command in a folder, output and typed-input
events, write, colors and font size, focus, terminate the process tree. `TerminalHost` puts one in the
node template and raises `Ready` once it's in the visual tree; `TerminalCanvasController` registers it
then. The one implementation, `WindowsTerminalSurface`, wraps `EasyTerminalControl`, which hosts Windows
Terminal's native C++/DirectX control inside an **HwndHost**; processes are started through ConPTY.
Another implementation (e.g. xterm.js in WebView2, decision record 0002) would only replace that folder
and the line in `TerminalHost.CreateSurface`. Consequences of the current one:

1. **No scaling**: an HwndHost ignores WPF's `RenderTransform`; when Nodify zooms, the HWND shrinks but
   the text doesn't. Fix: when the zoom changes, set the font size for that zoom via
   `ITerminalSurface.SetAppearance(..., fontSize)` (`TerminalFontSteps`, see the jump below).
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
   - **Zoom snapping (#34):** the font size is whole (the control's `SetTheme` takes a `short`) and the renderer rounds
     the cell width and line height to whole pixels. Measured with Cascadia Code at 96 DPI, they are
     `round(0.586 × px)` × `round(1.162 × px)`: 13 pt is 10 × 20 px, 11 pt 9 × 17. So at most zoom levels the
     live text came back wider or narrower, taller or shorter than the scaled snapshot when motion ended: up
     to 18 % (at 42 %), 4.5 % on average between 40 % and 150 %.
     - `TerminalFontSteps` computes each font size's cell from the font's metrics and the monitor DPI. It
       gives each size a zoom halfway between its width and height ratios to 13 pt.
     - After a zoom (not a pan or drag), the canvas eases to the nearest such zoom in 110 ms, around the
       mouse, still in snapshot mode. Then the live terminals return.
     - The difference left is at most 5.3 % (6 pt, at 47.5 %), at most 3.2 % from 60 % up, and none at
       60, 70, 100 and 110 %.
     - Measured at a zoom of 0.83: before, the text came back 8.4 % wider (9 px cells against a scaled
       8.3); it now settles at 87.5 % with +2.9 % / −2.9 % (9 × 17 px against 8.75 × 17.5).
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

- **What's saved:** nodes (id, workspace, profile, title, command, color, working directory, WSL or not, location,
  size), the workspaces (id, name, viewport) and which one is shown, recent directories and the last used profile.
  The top-level `viewport` is the shown workspace's, so an older version still opens where you were; a file from
  before workspaces opens as a single "Workspace 1". Terminal contents and
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

**Titles:** a Claude Code node takes its conversation's title, read with the context size from the transcript
(`TranscriptUsage`): a `/rename` (`custom-title`) always renames the node; Claude's own title (`ai-title`) only does
while the user hasn't named the node (**Rename** on the header's context menu). Titles already applied are saved with
the node, so a restart doesn't undo a name the user typed. `titlesFromClaude` in `settings.json` turns it off.

**Recent** (`Ctrl+Shift+O`) lists saved Claude Code conversations (`ClaudeConversations`): the 300 most recently written
transcripts in `<claude config dir>\projects\*\*.jsonl`, reading only each file's first 128 KB (folder, first prompt)
and last 64 KB (latest `custom-title` / `ai-title`), since transcripts add up to hundreds of MB. Picking one opens a
node in its folder with that session id, so it's resumed; one already on the canvas is gone to instead.

Programs that write OSC 7501 status reports without waiting for the terminal's answer (Codex) are read from
the terminal output by `ProgramStatusParser`; Allow types the program's own "Yes" key (`y` for Codex).

**Cursor CLI** nodes keep their conversation too (#97): the first time a node starts, its terminal waits ("Starting a
Cursor chat…") while `cursor-agent create-chat` makes an empty chat (`CursorChats`; its output must be the id alone
on one line). The id is saved as the node's `SessionId` and every start adds `--resume <id>`. Without an id (not
signed in, an update notice) the node starts unpinned; a profile that picks its own chat is left alone. Cursor and
Antigravity (`agy`) report no status, so their nodes show none, but they count as agents (Board, Send to agents).

**Claude Code inside WSL:** a distro can't reach 127.0.0.1 on Windows with WSL 2's default NAT network, and
listening on the WSL adapter would mean a firewall prompt. So `ClaudeHooks.PrepareWslArguments` writes *command*
hooks that pipe the event to Windows' `curl.exe` (run through WSL interop, it connects from the Windows side) and an
MCP stdio server that is a shell loop posting each JSON-RPC line the same way. Both read the URL and token from
`<node id>.endpoint` at every call: the port changes with every Kovan run, while an agent kept alive in tmux keeps
the settings it started with. The conversation is resumed by the shell (`$(ls ~/.claude/projects/*/<id>.jsonl …)`
picks `--resume` or `--session-id`), and the reported Linux transcript path is read through `\\wsl.localhost`.

**Codex and Gemini CLI inside WSL:** status needs nothing extra.
- Codex's OSC 7501 reports and the screen rules (Gemini) read the terminal, which works the same for a distro.
- The node knows its agent from the profile's executable (`AgentExecutable`); its command line is `wsl.exe`'s.
- Codex reaches the agent mesh through the same stdio bridge as Claude Code, written as `<node id>.wsl.mcp.sh` and given with
  `-c mcp_servers.kovan.command='sh' -c mcp_servers.kovan.args=['<script>']`.
  - These are TOML literal strings, so no double quote has to survive wsl.exe's command line.
  - The bridge skips blank lines.

## Notifications

While Kovan isn't the active window, an agent that starts waiting for the user or finishes its turn shows a Windows
notification (#80): "Claude needs you" with what it asks ("Allow Bash: npm test?"), or "Codex finished", with the
workspace if it's another one. Clicking it brings Kovan forward on that agent (switching workspace).
- **Summary:** `AgentNotifications` keeps what's pending since Kovan was last active and sums it up ("2 agents need
  you", "Also finished: …"), since a newer notification replaces the one on screen. Nodes closed or answered in the
  terminal meanwhile are dropped. Activating Kovan clears it.
- **How it's shown:** `TrayNotifier` uses the notification area (`Shell_NotifyIcon` with a balloon), which Windows 10
  and 11 show as a toast: no WinRT projection (~25 MB) or notification package. The icon is added with the first
  notification and removed when Kovan is activated, taking its notifications in the notification center along. Clicks
  come back to a message-only window. Focus assist holds them back like any app's.
- **Settings:** `settings.json` has `notifyWhenWaiting` and `notifyWhenDone` (both `true`). The taskbar button still
  flashes too.

## Keeping the computer awake

While at least one agent is working, Windows doesn't go to sleep on its own (`SleepBlocker`,
`SetThreadExecutionState` with `ES_SYSTEM_REQUIRED`), so a long task left running isn't cut off halfway. It ends as
soon as no agent works (waiting for the user counts as not working). The display can still turn off, and closing a
laptop's lid still does what the power settings say. `keepAwakeWhileWorking` in `settings.json` (`true`) turns it off.

## Settings

The Settings window (title bar, `Ctrl+,`; `SettingsViewModel`) shows what `settings.json` holds besides the panels and
worktree setup commands: theme, titles from Claude, keeping the computer awake, notifications, and the dictation
model and language (#92). Each change is saved at once, reading the file first so what the window doesn't show is
kept. The theme goes through the main window, which owns and saves it. Everything else is read where it's used, so it
takes effect without a restart.

## Status light

With **Status light on the screen's edge** on (Settings, off by default), a small always-on-top pill shows on the work
area's edge while Kovan isn't in front (inactive or minimized) and an agent works or waits.
- **What it shows:** a green dot with the number of agents working, a yellow one with the number waiting for you; a
  dot is dim at zero, so the count tells it without color.
- **Clicking it** brings Kovan forward on the next waiting agent.
- **Moving it:** drag it along the edge, or right-click to move it to the other edge or turn it off. Its place is
  saved (\`statusLightRightEdge\`, \`statusLightTop\`).
- **Focus:** the window is \`WS_EX_NOACTIVATE\` and \`WS_EX_TOOLWINDOW\`, so it never takes focus and isn't in Alt+Tab.
- **Accessibility:** the pill is a button (UI Automation Invoke, its help text the counts).

## Agents panel

A list of every node beside the canvas (left), grouped by working folder, with the agents waiting for the user first
(`ListCollectionView` with live sorting on `StatusOrder`). Each row shows the status, the context the conversation
uses (`TranscriptUsage` reads only what was appended to the transcript, off the UI thread, on `Stop`/`PostToolUse`
hooks) and running subagents (`SubagentStart`/`SubagentStop`). It updates only on status events.

The panel is square and flush with the window edge: a large rounded, bordered panel at this spot dropped canvas
motion from ~59 to ~34 fps. Keep large static chrome next to the canvas free of `CornerRadius`, and measure.

## Web preview nodes

A preview node (`TerminalNodeViewModel.IsBrowser`, saved with its `Url`) shows a WebView2 instead of a terminal. A
WebView2 is an HWND too, so `TerminalCanvasController` treats it like a terminal (one `Entry` with a `Host`): hidden
behind a snapshot while the canvas moves, put to sleep off screen, clipped to the canvas. Its snapshot comes from the
same single `PrintWindow(PW_RENDERFULLCONTENT)` capture of the window (it includes WebView2's content), retaken after
each navigation and every 2 s while shown, since a page changes without telling. The page is zoomed like the canvas
(`ZoomFactor`). All previews share one WebView2 environment (`<data dir>\WebView2`): one browser process group, about
25 MB per extra page. A terminal that prints a local address (`LocalUrls`: loopback only) offers **Preview** in its
header; the canvas menu has "New web preview here".

## Groups

A group (`TerminalNodeViewModel.IsGroup`, saved with `Group: true`; its `Title` is the name, `Width`/`Height` the whole
frame) is a Nodify `GroupingNode`: dragging its header moves the nodes inside it (Shift+drag moves only the frame),
the corner thumb resizes it. Its container gets `Panel.ZIndex = -1`, so it stays behind the nodes; since it's plain WPF
behind the terminals, airspace isn't an issue. The moved terminals go through snapshot mode like any other drag.
"Group the selected nodes" (canvas menu, shown when nodes are selected) frames them with room for the header and names
the group after their folder if they share one; "New group here" adds an empty frame. The header's name is an
editable text box; its × removes only the frame. Groups aren't terminals: not in the Agents panel, the terminal count,
keyboard navigation or the free-spot search for new nodes (a new node may be placed inside a group).

## Workspaces

Several canvases in one window, e.g. one per project, shown as tabs next to the toolbar buttons
(`WorkspaceViewModel`). All nodes stay in the one `MainViewModel.Nodes` collection, each with a `WorkspaceId`, so the
agent mesh, the Agents panel, status tracking and the waiting count keep covering every workspace. Switching only
changes which nodes are shown: nodes of the other workspaces get `IsInActiveWorkspace = false`, which makes their
Nodify container `Hidden` (not `Collapsed`: a collapsed node is never laid out, and its terminal wouldn't start until
its workspace was first shown) and not selectable or draggable, and `TerminalCanvasController` treats them like
off-screen terminals: their HWNDs sleep, their processes keep running. Each workspace remembers its viewport, and
the node last worked with on it. Keyboard navigation, "fit all", the free-spot search and grouping only look at the
shown workspace; going to a node on another one (Agents panel, "N waiting", Recent) switches to it first.

A tab shows how many of its agents are waiting or working, so another workspace can call for attention; the Agents
panel names the workspace of agents that aren't on the shown one. Double-click a tab to rename it; its right-click
menu closes it, after asking, together with its nodes (closing a worktree or tmux node still asks its own
question, and cancelling one keeps the workspace). A node's or group's header menu moves it to another workspace or a
new one; a group takes the nodes inside it along. `Ctrl+Shift+PgUp`/`PgDn` switch to the previous/next workspace
(`Ctrl+Alt+digit` would be AltGr on many keyboard layouts). Hidden workspaces cost nothing measurable while panning
and zooming: 12 terminals plus 12 more on a hidden workspace at the same spots ran the self-test at ~58-59 fps, the
same as the 12 alone.

## Note nodes

A note (`TerminalNodeViewModel.IsNote`, saved with its Markdown as `Note`) is plain WPF, so unlike terminals it
scales with the canvas and needs no snapshot. `NoteMarkdown` parses the small Markdown subset notes use (headings,
lists, task lists, quotes, fenced code, rules, bold/italic/code/links) line by line, and `NoteRenderer` turns it into a
`FlowDocument`; a task's checkbox toggles that line in the text. **Edit** shows the source in a text box. **Send to…**
pastes the note into a terminal as one bracketed paste, so an agent receives it as a single message to review and
submit. While the canvas moves, note bodies get a `BitmapCache` (`TerminalCanvasController.MotionChanged`): re-laying
out their text at every zoom step dropped zooming from ~59 to ~47 fps. Created from the canvas menu or `Ctrl+Shift+N`;
they aren't listed in the Agents panel and don't count as terminals.

## Agent mesh and Board

Agents coordinate through an MCP server inside Kovan (`MeshServer`, JSON-RPC over HTTP on the hook listener:
`POST /mcp/<node id>/<per-run token>`; the token is in the URL because not every agent can send headers to an MCP
server). Claude Code nodes get it with `--mcp-config <data dir>\hooks\<node id>.mcp.json`, Codex nodes with
`-c mcp_servers.kovan.url=…`; the user's own MCP servers are untouched. Tools: `workspace_status`, `agents_list`,
`send_message` / `inbox`, `task_list` / `task_create` / `task_claim` / `task_complete`, `lease_acquire` /
`lease_release` (advisory locks on files, folders or globs). State lives in `MeshBoard` (thread-safe; tasks and the
last 300 messages are saved with the workspace, leases expire). Messages are pulled, never typed into a terminal: when
an agent with unread messages gets a prompt, Kovan's answer to its `UserPromptSubmit` hook adds a one-line
`additionalContext` telling it to read its inbox. Messages are information, not instructions (the server's
instructions say so), size- and rate-limited. The **Board** tab of the right panel shows the tasks (the user can add,
start, finish, reopen and delete them, or drag a card onto another column), the leases and every message.

The Board is also the kanban for the user's own work (#16), kept in the panel rather than as a canvas node: a node
would be one more HWND-free but large WPF surface to snapshot and keep in view, and the panel already shows the same
tasks the agents see. **Give to an agent** (the person icon on a card) picks an agent on the canvas, pastes the task
into its terminal as one bracketed paste for the user to submit, and marks the task `Assigned` to that agent. From
then on Kovan moves it along with the agent's status, whatever reported it (hooks, OSC 7501): `todo` → `doing` when
the agent starts working, `doing` → `done` when it goes from working to idle or done. Waiting for an answer or a
permission isn't the end of a turn. A task an agent claimed itself (`task_claim`) isn't advanced this way; the agent
completes it. Moving a card back to *To do* makes it nobody's again.

## Send to agents

One prompt to several terminals at once, e.g. the same task to Claude Code, Codex and Gemini CLI to compare them
(#78). `Ctrl+Shift+S`, the send button in the Agents panel's header, or **Send to agents…** on the canvas's context
menu open `BroadcastWindow`.
- **Offered:** every workspace's terminals (not notes, groups or previews; not ones that failed to start or are still
  starting), this workspace's first, agents before shells.
- **Checked by default:** the selected nodes, or with none selected this workspace's agents.
- **Sending:** one bracketed paste per terminal (an agent takes it as one message). With **Press Enter** (the
  default), Enter goes to all of them 150 ms later, since some agents take an Enter that arrives with a paste as part
  of it.

## Pull requests of worktrees

A worktree node's branch is looked up with the GitHub CLI (`PullRequests`, `gh pr view <branch> --json …`, which
keeps the user's login) shortly after startup, every 3 minutes, after a push and when a turn ends (at most every 30 s
per node) (#86). Without `gh`, or for a worktree inside WSL, nothing is shown.
- **Shown:** next to the branch on the node's header and in the Changes panel: "PR #12 · checks failing", running,
  passed, draft, merged or closed. Check runs and commit statuses both count.
- **Failed checks:** **Send failed checks to the agent** pastes which checks failed and the last 120 lines of each
  failed Actions run's log (`gh run view <id> --log-failed`, at most 10 000 characters, without colors or control
  characters) into the agent, for the user to send.
- **Merged:** **Delete worktree** closes the node and deletes the worktree and branch, after saying what would be
  lost: commits made after the pull request's last commit, uncommitted files.

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

## Statistics

`Ctrl+Shift+Y` or the chart button in the toolbar opens Statistics in its own window (#88), so the canvas's terminals
don't cover it.
- **Source:** `Services/Stats/WorkStats` reads Claude Code's transcripts (`<claude config dir>\projects\*\*.jsonl`, each
  with its subagents' files) and Codex's sessions (`~\.codex\sessions`, or `%CODEX_HOME%`).
  - One `WorkSession` per conversation: folder (the project), title (`/rename`, Claude's title, or the first prompt),
    up to 40 prompts, stretches of work, and tokens per day and model.
  - Lines over 64 KB are tool output: only their timestamp and usage are taken, without parsing them.
- **Counting:**
  - **Time:** events more than 10 minutes apart end a stretch of work, and a stretch is at least a minute.
    Overlapping sessions are counted once (`Union`), and days split at midnight.
  - **Tokens:** a reply streamed in parts repeats its usage, so it's counted once per message id. Cache reads are
    shown apart from cache writes, input and output.
- **Cache:** `<data dir>\stats-cache.json`, keyed by file size and time; only changed files are read again.
  Measured here: 1.1 GB in 121 conversations took 3.7 s the first time and 23 ms from the cache.
- **The window** (`StatisticsWindow`, `StatisticsViewModel`):
  - **Period:** the last 7 or 30 days, 12 months, or a day picked on the calendar or the daily bars. It filters the
    headline numbers, the projects table, tokens by model and the session log (each session expands to its prompts).
  - **Calendar:** shaded by time or tokens in quartiles of the active days.
  - **Hours a day:** the last two weeks.
  - **Commits** (#99): `WorkCommits` runs `git log --all --no-merges` over the last year in each repository the
    conversations ran in, by the repository's configured `user.email`, after the sessions are shown (the count reads
    "…" until git answers). A commit seen twice (rebased, or through a worktree and its main checkout) counts once.
    They're a headline number, a column of the projects table and a list for the period.
  - **Copy as image:** the period, its numbers and the calendar, at twice the screen's resolution on the window's
    background, to the clipboard and `Pictures\Kovan Term`.
  - **Colors:** `ChartPalette` sets them for each theme. A project keeps its color whatever the period (the 7 with
    the most time overall; the rest are gray), and the calendar uses one hue from light to dark.

## Links in terminals

The terminal control finds links itself and opens one in the default browser on Ctrl+click. With **Open web links
in a preview next to the terminal** on (Settings, off by default), Kovan takes that click instead (#101).
- **The click:** the Ctrl+click's \`WM_LBUTTONDOWN\` and its release are caught in the thread's message hook (the same
  one shortcuts use) before the terminal's window gets them.
- **The link:** \`TerminalLinkReader\` reads the line under the point through the UI Automation text pattern
  (\`RangeFromPoint\`, off the UI thread), plus the next line for a link wrapped at the last column.
  \`TerminalLinks.At\` picks the link covering that column, without the sentence's closing punctuation.
- **Where it opens:** a web link opens in a preview beside the terminal, or goes to the open preview with that
  address. Other links (\`file:\`, \`mailto:\`) go to the system, as the terminal would send them. A Ctrl+click on no
  link does nothing.

## Dropping files on a terminal

Files dropped on a terminal are pasted into it as their paths, in one bracketed paste ending in a space (#90): an agent
such as Claude Code attaches an image it's given the path of.
- **Quoting:** paths are quoted for the shell (double quotes on Windows when they have spaces or `& ( ) ;`…;
  single quotes in WSL unless they're plain).
- **WSL:** a WSL terminal gets Linux paths (`/mnt/c/…`, or the distro's own path for a file inside it).
- **Control characters** are dropped, so a newline in a name can't run anything.
- **Text** dropped on a terminal is pasted as it is.
- **How it arrives:** the terminal's HWND has no drop target of its own, so the drop reaches the window's (WPF's), which
  hands it to the node body under the pointer.

## Plan usage

The bottom of the Agents panel shows the Claude plan's limits (5-hour session, week, per-model week), as Claude Code's
`/usage` does (`ClaudeUsage`): Claude Code's own OAuth token from `<claude config dir>\.credentials.json` is sent only to
`api.anthropic.com/api/oauth/usage`, every 5 minutes and after an agent's turn ends (at most once a minute). The
endpoint isn't documented: no login, an expired token (it's never refreshed here — that would rewrite Claude Code's
credentials), an error or an unexpected response hides the section. No limit is ever estimated. Codex's limits are only
available through its app-server, which Kovan doesn't run.

## Changes panel

Right of the canvas, for the current terminal (last clicked, navigated to, or the one in the middle at startup):
`git status` and the selected file's diff against `HEAD` (untracked files: against an empty file), through
`GitChanges`, which runs the `git` command line read-only (`GIT_OPTIONAL_LOCKS=0`, so it doesn't fight the agent's
own git commands for the index lock; output capped at 2 MB). It refreshes 700 ms after the agent's `PostToolUse` /
`Stop` hook or a `done`/`idle` status report, and on demand. The diff list is virtualized. For a WSL node the folder
is passed as `\\wsl.localhost\<distro>\...`, and `GitRunner` runs git inside the distro (`wsl.exe -d <distro> --cd
<path> --exec env GIT_OPTIONAL_LOCKS=0 ... git`): Git for Windows over `\\wsl.localhost` is slow and rejects the
repository as owned by someone else.

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

## Dictation

`Ctrl+Shift+Space` (or the microphone button in the toolbar) starts recording; pressing it again stops, and what was
said is typed into the terminal that was current when recording started, as one line without Enter, so it can be
checked before sending (#20).

- **Recording:** `WaveInRecorder` (`Interop/`) uses the waveIn API (winmm) at 16 kHz mono, which is what Whisper takes,
  so no audio library is needed. Recording stops by itself after 2 minutes; less than 0.25 s is ignored.
- **Speech to text:** `WhisperSpeechToText` runs whisper.cpp through [Whisper.net](https://github.com/sandrohanea/whisper.net)
  (MIT) on the CPU. Audio never leaves the computer. The model is loaded on first use and unloaded after 2 minutes
  without dictation, so it costs nothing while nobody's dictating.
- **Model:** downloaded once, after asking, from the whisper.cpp repository on Hugging Face into
  `<data folder>\models` (`SpeechModelStore`). `settings.json` picks it (`dictationModel`: `tiny`, `base` (default),
  `small`) and the language (`dictationLanguage`: `auto` (default), or a code such as `tr`, `en`).
- **Text:** non-speech annotations (`[BLANK_AUDIO]`, `(music)`) are dropped and the segments are joined into one line
  (`DictationText`).
- `DictationViewModel` holds the steps (download → recording → transcribing) and the toolbar status. It only knows
  `IAudioRecorder`, `ISpeechToText` and `ISpeechModelStore`, so it's tested with fakes.
- **Measured** (Release, Ryzen 7 5800X3D, 8 threads, sentences spoken by Windows' Turkish and English voices):

  | Model | Download | 3.4 s of Turkish | Working set while loaded |
  |---|---|---|---|
  | tiny | 78 MB | 1.0 s, two words slightly off | ~325 MB |
  | base | 148 MB | 0.9 s with `tr`, 1.5 s with `auto`; one letter off | ~370 MB |
  | small | 488 MB | 3.2–5.3 s; no better on these sentences | ~740 MB |

  The app is ~180 MB before the first dictation and back to ~215 MB once the model is unloaded. English ("Run the
  tests and fix the ones that fail.") came out exact with `base`. English words spoken with Turkish pronunciation
  ("pull request") weren't recognized by any model.
- `KOVAN_DICTATION_WAV=<file>` "records" that WAV file instead of the microphone, to test without speaking. The
  status log (`KOVAN_STATUS_LOG`) gets one line per dictation: audio length, time taken, memory, text.

## Voice commands

`Ctrl+Shift+K` (or the toolbar button next to the microphone) listens like dictation; pressing it again carries out
what was said instead of typing it (#21). Sentences are matched by fixed Turkish and English patterns on this
computer, not by a language model: [decision record 0004](decisions/0004-voice-commands-local-patterns.md).

- **Parsing:** `Services/Voice/VoiceCommands` turns the sentence into a `VoiceCommand`: an action, the nodes or
  workspaces it names (several if the name is ambiguous) and any text.
  - Text is folded first: lower case, Turkish letters to ASCII, punctuation to spaces, keeping each character's
    position, so a match also points into the original (the text sent to an agent keeps its letters and case).
  - Names are matched with Turkish case endings removed, a letter or two off, and by how they sound ("Klod",
    "Kodeks").
- **Actions:**

  | | Turkish | English |
  |---|---|---|
  | Go to the waiting agent | bekleyen ajana git | go to the waiting agent |
  | Go to a node | Codex'e git | go to Codex |
  | Tell an agent (pasted, then Enter) | Claude'a söyle testleri çalıştır / testleri çalıştırmasını Claude'a söyle | tell Claude to run the tests |
  | Answer a permission prompt | izin ver / reddet | allow / deny |
  | Close a node | Codex'i kapat | close Codex |
  | Zoom to the current or a named node | yakınlaş / Claude'a yakınlaş | zoom in / zoom to Claude |
  | Fit all | hepsini göster | show all |
  | New terminal (opens the dialog) / new note | yeni terminal aç / not al … | new terminal / take a note … |
  | Workspaces | sonraki alan / Kovan alanına geç | next workspace / switch to the Kovan workspace |
  | Panels | panoyu aç, değişiklikleri göster, ajanlar panelini kapat | show the board, show changes, hide the agents panel |

- **Asking first:** telling an agent something, allowing and closing ask first, showing what was heard.
  - "Allow" without a name answers the current node if it's asking, otherwise the one asking (or asks which).
  - A name that fits several nodes asks which one.
- **Carrying out:** `MainViewModel.RunVoiceCommandAsync` carries out the command and returns the status line text.
  Fit all and zoom need the editor, so they go to the view through `ViewCommandRequested`.
- **Whisper prompt:** while listening for a command, Whisper gets a prompt of sample commands that use the canvas's
  node and workspace names (`VoiceCommands.Prompt`), so it spells them as written.
- **Measured** (Windows' voices, through the real app, `dictationLanguage` `auto`): 8 of 8 commands carried out.
  - Turkish, with English names said the Turkish way ("Kodeks'e git", "Klod'a söyle testleri çalıştırsın", "Kodeks'i
    kapat", "hepsini göster", "bekleyen ajana git").
  - English ("tell Codex to check the build", "go to the waiting agent", "show all").
  - With `tr`, English commands came out as Turkish sounds ("Tell Codex'e çekti. Build").
  - Without the prompt, names came out as sounds ("Cılavu deya söyle").
  - Every command takes 0.8–1.3 s from the second press.
- The status log (`KOVAN_STATUS_LOG`) gets each command heard and what it was understood as.
