# 0004: Voice commands with local patterns, not a language model

- **Status:** Accepted
- **Date:** 2026-11-19
- **Related:** #21, #20

## Context

Voice commands ("go to the waiting agent", "tell Claude to run the tests") turn a dictated sentence into an action
on the canvas. Speech is already transcribed on this computer (Whisper, #20), so this decision is only about the
step after that: what turns the sentence into an action. The issue suggested a language model with the actions
as tools, and asked for local versus cloud to be decided here.

| Option | Speed | What leaves the computer | Cost | Understands |
|---|---|---|---|---|
| **Local patterns** (Turkish and English) | instant | nothing | none | the fixed commands, with loose name matching |
| Local language model (llama.cpp) | 1–5 s | nothing | a 2–5 GB download and as much RAM while loaded | free-form sentences |
| The user's Claude Code (`claude -p`) | 5–10 s (process start and a turn) | the sentence and the terminal names | the Claude plan's usage | free-form sentences |
| Anthropic API | 1–3 s | the sentence and the terminal names | an API key and per-call cost | free-form sentences |

- **Size:** a local model goes against the reason this app exists: light on memory (decision record 0001). With
  ~180 MB for the app and ~370 MB while Whisper is loaded, a language model would be several times the rest.
- **Speed:** waiting 5–10 s after every "go to Codex" defeats the point of saying it.
- **Scope:** the actions themselves are few and fixed: go to a node or the waiting agent, tell an agent something,
  allow or deny, close, zoom, fit all, new terminal or note, switch workspace, show or hide a panel. Each has a
  small number of ways to be said.

## Decision

Voice commands are matched by **fixed Turkish and English patterns on this computer** (`Services/Voice/VoiceCommands`).

- **Nothing leaves the computer;** commands run instantly and are covered by unit tests (every pattern has examples).
- **Names are matched loosely:**
  - Turkish case endings: "Codex'e", "Codexe", "Gemini'ye".
  - A letter or two off.
  - How a Turkish speaker says English names: "Klod" is Claude, "Kodeks" is Codex.
- **Whisper is told what to expect:** sample commands that use the names on the canvas, which makes it spell them
  as they're written. A bare list of names made it write the Turkish words around them as English, and it was
  sometimes written back on short recordings.
- **Asking first:** sending text to an agent, allowing a permission and closing a terminal ask first, with what was
  heard, since a misheard command must not do any of these on its own. Everything else is undone by another command.
- **Language model later:** the parser returns a `VoiceCommand` (an action and its targets), the same shape a
  language model's tool call would have. A model could be added later, as an opt-in fallback for sentences the
  patterns don't understand, without changing what carries the actions out.

## Consequences

- Free-form sentences ("could you have the second agent look at the failing build") aren't understood. The status
  line says what was heard and gives examples.
- New commands need new patterns, in both languages, with tests.
- English commands need `dictationLanguage` `auto` (the default): with `tr`, Whisper writes English as Turkish
  sounds. Turkish commands work with both.
