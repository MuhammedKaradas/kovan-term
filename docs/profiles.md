# Profiles

A profile defines which program a new terminal starts with. You pick a profile and a working
directory in the dialog opened by the "+ New terminal" button or by right-clicking the canvas →
"New terminal here…".

## Built-in profiles

| Id | Name | Runs |
|---|---|---|
| `powershell` | PowerShell | `powershell.exe -NoLogo` |
| `pwsh` | PowerShell 7 | `pwsh.exe -NoLogo` |
| `cmd` | cmd | `cmd.exe` |
| `claude` | Claude Code | `claude` |
| `codex` | Codex | `codex` |
| `gemini` | Gemini CLI | `gemini` |

Executables are resolved through `PATH` and `PATHEXT`. Profiles whose program isn't found are shown
dimmed and can't be selected. `.cmd`/`.bat` files (which is how npm installs CLIs) are started through
`cmd.exe /k`, so the terminal stays usable after the agent exits.

## Adding your own profiles

Create `%APPDATA%\KovanTerm\profiles.json`. A profile with the same `id` overrides the built-in one;
a new `id` is added to the list. Comments and trailing commas are allowed.

```jsonc
[
  // Start Claude Code in plan mode
  { "id": "claude", "name": "Claude Code (plan)", "executable": "claude", "arguments": "--permission-mode plan", "color": "#D97757" },
  // A new agent
  { "id": "aider", "name": "Aider", "executable": "aider", "color": "#14B014" },
  // A full path works too
  { "id": "git-bash", "name": "Git Bash", "executable": "C:\\Program Files\\Git\\bin\\bash.exe", "arguments": "--login -i" }
]
```

| Field | Required | Description |
|---|---|---|
| `id` | ✔ | Unique id; stored with the node in the workspace |
| `name` | ✔ | Name shown in the list and in the node header |
| `executable` | ✔ | Program name (looked up in `PATH`) or full path |
| `arguments` | | Command-line arguments |
| `color` | | Accent color under the node header (`#RRGGBB`) |

If the file can't be read, the app starts with the built-in profiles only. Changes take effect after
restarting the app.

## When a workspace is opened

For nodes with a known profile, the command is rebuilt from the current profile, so the right path is
used even if the CLI has moved. If the program can't be found, the terminal isn't started and the node
shows "Couldn't start terminal" instead.
