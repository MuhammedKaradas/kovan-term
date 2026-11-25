# Usage statistics

Kovan Term can count which of its features are used, to show what's worth working on. It's **off unless you agree**:
release builds ask once (together with [crash reports](crash-reports.md)), and you can change it in Settings → Privacy.
Builds from source never ask or count anything.

## What's counted

Only how many times a day each of these happened. Nothing else can be counted: Kovan ignores any other name, so file
names, paths, prompts, terminal text and agents' conversations can't end up here.

| Event | Counted when |
|---|---|
| `app.start` | Kovan was opened |
| `node.shell` | A shell terminal (PowerShell, cmd, WSL…) was opened |
| `node.claude` | A Claude Code node was opened (including DeepSeek and Ollama profiles) |
| `node.codex` | A Codex node was opened |
| `node.antigravity` | An Antigravity node was opened |
| `node.gemini` | A Gemini CLI node was opened |
| `node.cursor` | A Cursor CLI node was opened |
| `node.opencode` | An OpenCode node was opened |
| `node.qwen` | A Qwen Code node was opened |
| `node.other` | A node with another program was opened |
| `node.note` | A note was added |
| `node.preview` | A web preview was added |
| `node.group` | A group was made |
| `profile.deepseek` | The Claude Code (DeepSeek) profile was used |
| `profile.ollama` | The Claude Code (Ollama) profile was used |
| `profile.custom` | A profile from your `profiles.json` was used (not which one) |
| `feature.worktree` | A node was started in a new git worktree |
| `feature.broadcast` | A prompt was sent to several agents |
| `feature.recent` | A conversation was reopened from Recent |
| `feature.outside` | Showing sessions started outside Kovan was turned on |
| `feature.outside.open` | A session started outside Kovan was opened in Kovan |
| `feature.allow` | Allow or Deny was pressed in Kovan |
| `feature.dictation` | Dictation was used |
| `feature.voice` | A voice command was used |
| `feature.statistics` | The Statistics window was opened |
| `feature.board` | A task was added to the Board |
| `feature.pull_request` | Create pull request or Merge was chosen on a worktree node |
| `feature.workspace` | A workspace was added |

The counts are kept in `%APPDATA%\KovanTerm\usage.json`, which you can read at any time.

## When it's sent

When Kovan starts, it sends the days that are over (not today) in one request, then removes them from `usage.json`.
So at most one request a day, and none on days Kovan isn't opened. If you're offline or the server has trouble, the
days wait for the next start. Days older than 30 days are dropped without being sent.

## For the server

```http
POST <endpoint>
Content-Type: application/json; charset=utf-8
User-Agent: KovanTerm/<version>

{"schema":1,"installId":"1b2c3d4e-…","appVersion":"0.2.0","os":"Windows 10.0.26200.0",
 "days":[{"date":"2026-11-19","counts":{"app.start":2,"node.claude":3}},
         {"date":"2026-11-20","counts":{"feature.dictation":1}}]}
```

- `installId` is the same random id crash reports use, made when you agreed to either. `date` is your local date.
- Answer **2xx** once it's stored: the days are removed.
- Answer **4xx** (except 429) to refuse: the days are removed too, and not sent again.
- Answer **429** or **5xx** to have them sent on a later start.
- A day can arrive twice if the answer is lost. Use (`installId`, `date`) as the key and keep the latest.

The endpoint is set when building: `dotnet publish … -p:KovanUsageEndpoint=https://…` (HTTPS only). The release
workflow takes it from the repository variable `USAGE_STATS_ENDPOINT`.
