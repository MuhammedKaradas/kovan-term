# Roadmap

See the [milestones](https://github.com/MuhammedKaradas/kovan-term/milestones) on GitHub for the live
status. This document explains the big picture and why things are ordered the way they are.

## v0.1 Foundation: a canvas you can use every day

Goal: open Kovan Term every day and work with agents in it.

| # | Item | Status |
|---|---|---|
| #1 | Move to MVVM and set up the project structure | ✅ |
| #2 | Save and restore the workspace | ✅ |
| #3 | Agent profiles and a "new terminal" menu | ✅ |
| #4 | Resize terminal nodes | ✅ |
| #5 | Snapshot mode improvements (smoothness) | ✅ |
| #6 | Put off-screen terminals to sleep | ✅ |
| #7 | Keyboard shortcuts and focus navigation | ✅ |
| #8 | Research the terminal dependency risk | ✅ ([0002](decisions/0002-terminal-dependency-risk.md)) |
| #9 | Installer package and release publishing | ✅ |
| #24 | Closed terminals kept running in the background (bug) | ✅ |
| #35 | Canvas scrolled by itself when a terminal took focus (bug) | ✅ |
| #25 | Modern Windows 11 look with WPF-UI | |
| #34 | Small text "jump" when a gesture ends | |

## v0.2 Agent awareness

Goal: see at a glance which agent is working and which one is waiting for you.

| # | Item |
|---|---|
| #10 | Detect agent status through a Claude Code hook bridge |
| #11 | Waiting-agent notifications and a control panel |
| #12 | Codex and Gemini CLI status support |
| #13 | Resume agent sessions |
| #14 | Usage and quota tracking |

## v0.3 Workspace

Goal: plans, tasks, previews and code on the same canvas.

| # | Item |
|---|---|
| #15 | Note node (markdown) |
| #16 | Task board (kanban) node |
| #17 | Web preview node (WebView2) |
| #18 | Git worktree integration |
| #19 | Groups (spaces) and per-project separation |

## v0.4 Voice

Goal: talk instead of typing, and drive the canvas by voice. This is what sets Kovan Term apart from
similar projects.

| # | Item |
|---|---|
| #20 | Local dictation (Whisper.net) |
| #21 | Voice command planner |

## Not scheduled yet

| # | Item |
|---|---|
| #30 | Put the terminal control behind an interface |

## Why this order

1. **Foundation before features:** any feature added before persistence and MVVM would have had to be
   rewritten later.
2. **Agent awareness before voice:** most voice commands ("go to the waiting agent") need to know the
   agents' status.
3. **Smoothness is measured in every release:** being lightweight is the reason this project exists.
   New node types (WebView2 included) must work with snapshot mode.
