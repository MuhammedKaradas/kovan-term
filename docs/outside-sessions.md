# Claude Code sessions outside Kovan

Kovan can show Claude Code sessions you started somewhere else, such as VS Code or Windows Terminal, in the Agents
panel under **Elsewhere**. You see whether each one is working or waiting for you, and Kovan notifies you when one
needs you. It's off until you turn it on in **Settings → Notifications → Show Claude Code sessions started outside
Kovan**.

## What it changes

Kovan can only learn about a session it didn't start through Claude Code's hooks. When you turn this on, Kovan adds
one command to your `~/.claude/settings.json` for these events: `SessionStart`, `UserPromptSubmit`,
`PermissionRequest`, `Notification`, `PostToolUse`, `Stop`, `StopFailure` and `SessionEnd`.

```json
{
  "type": "command",
  "command": "\"C:/Apps/Kovan/KovanLaunch.exe\" --hook \"C:/Users/me/AppData/Roaming/KovanTerm/outside.endpoint\"",
  "timeout": 5,
  "async": true
}
```

- Your other settings and hooks stay exactly as they are.
- Turning the setting off takes Kovan's entries out again.
- If the file isn't valid JSON, Kovan doesn't touch it and tells you so.

## What the hook does

`KovanLaunch.exe --hook` sends the hook's input to Kovan, at the address in `outside.endpoint`. Kovan writes that file
when it starts and deletes it when it closes, because the port changes with every run.

- **When Kovan is closed,** the file isn't there and the hook returns at once.
- **It never slows a session down.** The hook runs in the background (`async`), prints nothing and never makes a
  decision, so Claude Code behaves exactly as it would without it.
- **Only this Kovan's events get in.** The request carries a token that changes with every run, and Kovan listens
  only on `127.0.0.1`.

If you move the Kovan folder, Kovan updates the path in the hook the next time it starts.

## In the Agents panel

- **Open** starts the conversation on the canvas where it left off (`claude --resume`). The session in the other
  window keeps running.
- **Hide** (right-click) removes the row; it comes back when the session reports again.
- A session drops off the list when it ends, or after 6 hours without a report (for example, if its window was closed).
- Sessions Kovan started itself aren't listed here; they already have their nodes.

Kovan can't type into another program's terminal, so there are no Allow or Deny buttons here: answer in the session's
own window.
