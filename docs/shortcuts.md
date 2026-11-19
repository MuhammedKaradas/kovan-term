# Keyboard shortcuts

Shortcuts work even while a terminal has focus. To stay out of the way of keys that terminal programs
use, most of them use `Ctrl+Shift` (e.g. `Ctrl+T` transposes characters in bash/readline, so it's left alone).

| Shortcut | Action |
|---|---|
| `Ctrl+Tab` | Next terminal: pans the viewport to it and gives it focus |
| `Ctrl+Shift+Tab` | Previous terminal |
| `Ctrl+Shift+F` | Fit all terminals |
| `Ctrl+Shift+Enter` | Zoom to the current terminal (~90% of the visible area, at most 100% zoom) |
| `Ctrl+Shift+T` | New terminal |
| `Ctrl+Shift+A` | Show/hide the Agents panel |
| `Ctrl+Shift+G` | Show/hide the Changes panel (git changes of the current terminal's folder) |
| `Ctrl+Shift+O` | Reopen a recent Claude Code conversation as a new terminal |
| `Ctrl+Shift+B` | Show/hide the Board: the agents' tasks, messages and leased files |
| `Ctrl+Shift+N` | New note (Markdown) |
| `Ctrl+Shift+PgDn` | Show the next workspace |
| `Ctrl+Shift+PgUp` | Show the previous workspace |
| `Ctrl+Shift+Space` | Dictate into the current terminal; press again to stop and type what was said |
| `Ctrl+Shift+K` | Voice command ("go to the waiting agent", "Claude'a söyle testleri çalıştır"); press again to carry it out |

**Mouse:**

| | |
|---|---|
| Double-click a header | Zoom to that terminal and focus it |
| Click a row in the Agents panel | Go to that terminal and focus it |
| Right-click the canvas | "New terminal here…" |
| Double-click a workspace tab | Rename it (right-click: close it) |
| Right-click a node's header | Move it to another workspace |
| Bottom-right grip | Resize the terminal |
| Wheel | Zoom in / out |
| Right-drag | Pan the canvas |

## Navigation order

`Ctrl+Tab` cycles through terminals in reading order: row by row, left to right within a row.
Terminals whose top edges are within 120 units of each other count as the same row. It wraps around at
the end. The "current" terminal is the one you last clicked or navigated to; if there isn't one,
navigation starts from the terminal closest to the center of the viewport.

## How it works

While a terminal has focus, keys go straight to the terminal's native window (HWND) and WPF
`KeyBinding`s never see them. The terminal window lives on the UI thread, so key messages are caught in
`ComponentDispatcher.ThreadPreprocessMessage` before the terminal gets them
(`Views/MainWindow.Shortcuts.cs`). The shortcut list is in `Views/Shortcuts.cs`; keep it in sync with
this document.
