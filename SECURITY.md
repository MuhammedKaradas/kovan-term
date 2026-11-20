# Security policy

## Reporting a vulnerability

Please **don't open a public issue** for a security problem. Report it privately through GitHub instead:
**Security → Report a vulnerability** on this repository
([direct link](https://github.com/MuhammedKaradas/kovan-term/security/advisories/new)).

Include what you found, how to reproduce it, and what an attacker could do with it. You'll get an answer within a
week. Once a fix is released, you'll be credited in the advisory unless you'd rather not be.

## Supported versions

Only the latest release gets security fixes.

## What's in scope

Kovan Term runs AI agents that read and write your files, so these matter most:

- **The hook and agent mesh server:** it listens on `127.0.0.1` on a random port with a per-run token. Reaching it
  without the token, from another user or from a web page, is in scope.
- **What's typed into terminals:** text pasted from notes, dropped files, voice commands, the Board or another agent.
  Anything that runs a command without the user doing so is in scope.
- **Files Kovan writes:** hook settings and MCP configs in the data folder, workspaces, the statistics cache.
- **Downloads:** the speech model, and the release packages.

Bugs in the agents themselves (Claude Code, Codex, Gemini CLI…) belong to their own projects.
