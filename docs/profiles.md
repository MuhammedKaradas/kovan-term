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

## WSL profiles

For every installed WSL distro (`wsl --list`, Docker Desktop's internal distros excluded) two more
profiles are added:

| Id | Name | Runs |
|---|---|---|
| `wsl-<distro>` | `<distro> (WSL)` | the distro's login shell |
| `claude-wsl-<distro>` | `Claude Code (<distro>)` | `claude` inside the distro |

The working directory of a WSL profile is a Linux path (`/home/me/code`) or `~` for the Linux home
folder; it's passed to `wsl.exe --cd`. Windows paths work too, but file access through `/mnt/c` is
slow, so keep repos inside the distro. **Browse…** opens the distro's files (`\\wsl.localhost\<distro>`)
and turns the picked folder back into a Linux path. When you switch between Windows and WSL profiles,
the dialog swaps in a recent directory of the matching kind.

Commands run in an interactive login shell (`$SHELL -lic`), so `PATH` entries from `~/.profile` and
`~/.bashrc` (`~/.local/bin`, nvm…) apply and the Linux CLI is used rather than the Windows one WSL
appends to `PATH`. If the command isn't found, the terminal says so; after the command exits, a login
shell opens in the same terminal.

Before a WSL terminal starts, Kovan boots its distro and, if the distro runs systemd, waits until systemd
has finished starting (at most 4 minutes); the node shows "Starting <distro>…" meanwhile. WSL holds new
login sessions while systemd is still starting, so without this wait the terminal would just stay blank.
If it takes long, a service is slow to start: `systemctl list-jobs` inside the distro shows which one.

Not yet available for WSL agents: status hooks, the agent mesh, conversation resume and the Changes
panel.

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
  { "id": "git-bash", "name": "Git Bash", "executable": "C:\\Program Files\\Git\\bin\\bash.exe", "arguments": "--login -i" },
  // Codex inside WSL (leave "distro" out for the default distro)
  { "id": "codex-wsl", "name": "Codex (WSL)", "executable": "codex", "wsl": true, "distro": "Ubuntu", "color": "#10A37F" }
]
```

| Field | Required | Description |
|---|---|---|
| `id` | ✔ | Unique id; stored with the node in the workspace |
| `name` | ✔ | Name shown in the list and in the node header |
| `executable` | ✔ | Program name (looked up in `PATH`) or full path |
| `arguments` | | Command-line arguments |
| `color` | | Accent color under the node header (`#RRGGBB`) |
| `wsl` | | `true` runs `executable` inside WSL; an empty `executable` opens the login shell |
| `distro` | | WSL distro; the default distro if left out |

If the file can't be read, the app starts with the built-in profiles only. Changes take effect after
restarting the app.

## When a workspace is opened

For nodes with a known profile, the command is rebuilt from the current profile, so the right path is
used even if the CLI has moved. If the program can't be found, the terminal isn't started and the node
shows "Couldn't start terminal" instead.
