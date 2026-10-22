# 0001: .NET/WPF and the native terminal control

- **Status:** Accepted
- **Date:** 2026-10-20

## Context

Similar open-source apps (OpenCove, TermCanvas) use Electron + React + xterm.js. They are feature-rich
but heavy and occasionally stutter. Our goal is a lightweight, smooth app on Windows.

## Options considered

1. **Write a plugin for OpenCove**: the fastest route, but Electron's weight remains.
2. **Tauri + xterm.js**: lighter than Electron, but terminal rendering still happens in a web view.
3. **WPF + xterm.js in WebView2**: no zoom problem, but the weight comes back.
4. **WPF + Windows Terminal's native control (HwndHost)**: the lightest and fastest terminal; comes
   with zoom and airspace constraints.

## Decision

Option 4. In a one-day prototype with 4 terminals + Claude Code, the app used ~150-170 MB of RAM.
The zoom constraint was solved by scaling the font size, panning stutter by "snapshot mode", and
smoothness was judged acceptable.

## Consequences

- Windows only.
- WPF content can't be drawn on top of a terminal; the UI has to be designed around that.
- We depend on the unofficial `CI.Microsoft.Terminal.Wpf` package; if it breaks, a migration plan to
  option 3 should be ready (see [0002](0002-terminal-dependency-risk.md)).
