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
| `claude-deepseek` | Claude Code (DeepSeek) | `claude` on DeepSeek's API; asks for your DeepSeek API key once |
| `claude-ollama` | Claude Code (Ollama) | `claude` on the local Ollama, with a model picked in the dialog |
| `codex` | Codex | `codex` |
| `gemini` | Gemini CLI | `gemini` |
| `cursor` | Cursor CLI | `cursor-agent` |
| `antigravity` | Antigravity | `agy` |

Executables are resolved through `PATH` and `PATHEXT`. Profiles whose program isn't found are shown
dimmed and can't be selected. `.cmd`/`.bat` files (which is how npm installs CLIs) are started through
`cmd.exe /k`, so the terminal stays usable after the agent exits.

## WSL profiles

For every installed WSL distro (`wsl --list`, Docker Desktop's internal distros excluded) four more
profiles are added:

| Id | Name | Runs |
|---|---|---|
| `wsl-<distro>` | `<distro> (WSL)` | the distro's login shell |
| `claude-wsl-<distro>` | `Claude Code (<distro>)` | `claude` inside the distro |
| `codex-wsl-<distro>` | `Codex (<distro>)` | `codex` inside the distro |
| `gemini-wsl-<distro>` | `Gemini CLI (<distro>)` | `gemini` inside the distro |

Kovan can't tell whether an agent is installed in a distro without starting it, so all four are listed. If the
program isn't on the distro's login shell PATH, the terminal says so and stays open at the shell.

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

### Keep running when Kovan closes

For WSL profiles the New terminal dialog has **Keep running when Kovan closes**. The program then runs in
a hidden tmux session (`kovan-<node id>` on Kovan's own tmux server, `tmux -L kovan`, without your
`~/.tmux.conf`). Closing Kovan leaves it running, and on the next start the node attaches to it again,
with its output. tmux is set up to stay out of the way: no status bar, no prefix key (Ctrl+B reaches the
agent), no Esc delay; the mouse wheel scrolls tmux's history (hold Shift to select text). tmux must be
installed in the distro (`sudo apt install tmux`); without it the terminal says so and runs normally.

Closing such a node asks whether to stop the session or leave it running. Sessions left running show up
the next time Kovan starts (only running distros are checked), with the choice to open them again as
nodes or stop them. To see them yourself: `tmux -L kovan ls` inside the distro.

Claude Code inside WSL gets the same as on Windows: its status in the node header and the Agents panel, the
agent mesh (Board, messages, leases), its context size, and its conversation resumed when the node starts again.
WSL 2's default network can't reach Kovan's local listener, so the hooks and the mesh go through Windows'
own `curl.exe`, which WSL can run; nothing has to be installed in the distro and no firewall rule is needed.
The Changes panel works for repositories inside the distro too (git runs there; it must be installed in the
distro), except for the home folder `~` and profiles without a `distro`. Not yet for WSL: Codex/Gemini hooks.

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
  { "id": "codex-wsl", "name": "Codex (WSL)", "executable": "codex", "wsl": true, "distro": "Ubuntu", "color": "#10A37F" },
  // Claude Code inside WSL that keeps running when Kovan closes
  { "id": "claude-wsl-tmux", "name": "Claude Code (Debian, tmux)", "executable": "claude", "wsl": true, "distro": "Debian", "persistent": true }
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
| `persistent` | | WSL only: `true` ticks "Keep running when Kovan closes" by default (see below) |
| `modelSource` | | `"ollama"`: the dialog lists the local Ollama's models to pick from |
| `environment` | | Environment variables for this profile's terminals only; see [below](#environment-variables-and-api-keys) |

If the file can't be read, the app starts with the built-in profiles only. Changes take effect after
restarting the app.

## Environment variables and API keys

`environment` sets variables for the profile's terminals only, for example to point Claude Code at another
provider's Anthropic-compatible API:

```jsonc
{
  "id": "claude-other", "name": "Claude Code (other provider)", "executable": "claude", "color": "#D97757",
  "environment": {
    "ANTHROPIC_BASE_URL": "https://api.example.com/anthropic",
    "ANTHROPIC_AUTH_TOKEN": "secret:example"
  }
}
```

- `%NAME%` in a value is replaced with that variable from Kovan's environment (`"PATH": "C:\\tools;%PATH%"`). An empty value removes the variable.
- A value `secret:<key>` is an API key kept in **Windows Credential Manager** as `KovanTerm/<key>`, never in
  `profiles.json`. The first time a terminal needs it, the terminal asks for it (what you paste isn't shown) and
  saves it. Several profiles can share a key by using the same name. To change or remove it, open Credential
  Manager > Windows Credentials.
- In WSL the variables are passed into the distro (through `WSLENV`). A "Keep running" session that was already
  running keeps the variables it was started with.

Such a terminal is started through `KovanLaunch.exe` (next to `KovanTerm.exe`): it sets the variables, reads the
keys, then runs the program and waits for it. The variables aren't saved in the workspace, and keys don't appear
on any command line.

## Other model providers

Claude Code can run on another provider's Anthropic-compatible API; Kovan's status, Allow/Deny, the agent mesh and
resuming work the same, since it's still Claude Code. The Claude plan panel only shows your Claude account's usage.

- **DeepSeek** (`claude-deepseek`): create an API key at [platform.deepseek.com](https://platform.deepseek.com)
  (usage is billed by DeepSeek). The first terminal asks for it and keeps it in Credential Manager as
  `KovanTerm/deepseek`. Models follow [DeepSeek's guide](https://api-docs.deepseek.com/quick_start/agent_integrations/claude_code).
- **Ollama** (`claude-ollama`): install [Ollama](https://ollama.com) 0.14 or later and get a model
  (`ollama pull qwen3-coder`). The New terminal dialog lists the installed models; the one picked is saved with the
  node. Nothing leaves your computer with a local model. Small models struggle with agent work: pick a larger one
  with a 64k+ context window if your machine can run it.

To change a setting, add a profile with the same id to `profiles.json` (for example another `ANTHROPIC_BASE_URL`).

## When a workspace is opened

For nodes with a known profile, the command is rebuilt from the current profile, so the right path is
used even if the CLI has moved. If the program can't be found, the terminal isn't started and the node
shows "Couldn't start terminal" instead.
