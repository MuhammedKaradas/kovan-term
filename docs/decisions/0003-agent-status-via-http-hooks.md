# 0003: Agent status through per-session HTTP hooks

- **Status:** Accepted
- **Date:** 2026-11-16
- **Related:** #10, #11

## Context

A node should show whether its agent is working, waiting for the user or done, and let the user answer a
permission prompt without clicking into the terminal. Claude Code can report this in three ways:

1. **OSC 7501 (Program Status Protocol).** Since 2.1.295 Claude Code writes its status into the terminal
   stream, but only after the terminal answers its `OSC 7501 ; ?` probe. Tested with the ConPTY Kovan ships
   (1.24): the reports pass through on output, but the probe's answer can't reach Claude in time. Conhost
   drops OSC sequences written to the input pipe; encoded as win32-input-mode key events they arrive, but
   after conhost's own DA1 reply, which Claude takes as "no more answers". `claude --debug` logs
   `programStatus=no (probe: no reply to OSC 7501 ; ?)`.
2. **Hooks.** Claude Code calls hooks on lifecycle events (`PreToolUse`, `PermissionRequest`, `Stop`…).
   Since recent versions a hook can be an HTTP POST (`"type": "http"`), so no helper executable is needed.
3. **Reading the screen or the transcript files.** Works for any agent, but is heuristic and needs to be
   kept up to date with every UI change.

## Decision

Hooks, delivered over HTTP to Kovan:

- `AgentHookServer` listens on `127.0.0.1` with a random port. A plain socket is used because
  `HttpListener` needs a URL reservation (admin) on Windows. Each run has a random token, sent in the
  `X-Kovan-Token` header; other requests get 403.
- For every Claude Code node Kovan writes `<data dir>\hooks\<node id>.json` and starts Claude with
  `--settings <that file>`. The file is rewritten at every launch (the port changes) and deleted when the
  node closes. **The user's own `~/.claude/settings.json` is never modified**; Claude merges these hooks
  with the user's.
- The hooks never return a decision: Claude behaves exactly as without Kovan. If Kovan isn't listening,
  the hook fails within a second and Claude carries on (it prints a warning).
- `AgentStatusReducer` maps events to the node's status. `PermissionRequest` → waiting (with the tool);
  Allow/Deny are offered only for yes/no tool permissions, not for `AskUserQuestion` or `ExitPlanMode`.
- Allow types `1` (the "Yes" row wherever the highlight is), Deny types Escape.
- Refusing a prompt with Escape sends **no** hook. So when the user types Enter, Escape or a digit into a
  waiting node's terminal, the prompt counts as answered (`PromptKeys`); an approval is followed by
  `PostToolUse`, which sets "working" again.
- Kovan removes Claude Code's session markers (`CLAUDECODE`, `CLAUDE_CODE_MESSAGING_SOCKET`…) from its
  environment at startup, so agents started in Kovan don't think they're nested in the session that
  launched Kovan.

## Consequences

- Status works for Claude Code only. Other agents need their own source (e.g. Codex's app-server
  protocol, or screen rules as a fallback) — separate issues.
- A user profile that passes its own `--settings` gets two; Claude uses both.
- The hook URL contains the node id; the token keeps other local programs from faking events.
- If Claude Code ever forwards OSC 7501 through ConPTY (or adds a way to force it), it can become a second
  source without changing the UI.
