# Contributing

## Branches

- `main` must always build and run.
- Every change is made on its own branch, tied to an issue:
  - `feature/<issue>-short-name` → `feature/12-save-workspace`
  - `fix/<issue>-short-name` → `fix/31-zoom-flicker`
  - `perf/…`, `docs/…`, `chore/…` for the matching kinds of work
- Branches are merged into `main` through a PR with **squash merge**. The PR title follows the commit
  convention below.

## Commit messages

We use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/), in English.

```
<type>(<scope>): <description>

[optional body: why and what]

[optional footer: Closes #12]
```

> Commits before 2026-10-22 were written in Turkish; they're left as they are.

### Types

| Type | When |
|---|---|
| `feat` | New feature |
| `fix` | Bug fix |
| `perf` | Performance improvement |
| `refactor` | Code change that doesn't change behavior |
| `docs` | Documentation only |
| `test` | Adding or fixing tests |
| `build` | Build, dependencies, packaging |
| `ci` | GitHub Actions etc. |
| `chore` | Other maintenance |

### Scopes

`canvas`, `terminal`, `agent`, `voice`, `workspace`, `ui`, `settings`. Leave the scope out if none fits.

### Rules

- The description starts with a lowercase letter, uses the imperative mood ("add", "fix"), has no
  trailing period and is at most 72 characters.
- **Simple change → one line.** A change that alters behavior, isn't self-explanatory or touches
  several parts → explain the *why* and the important details in the body.
- For breaking changes, add `!` after the type and a `BREAKING CHANGE:` footer.
- No attribution trailers in commits (`Co-Authored-By`, "Generated with …", etc.).

### Examples

Simple:

```
fix(canvas): keep the font size from dropping below 4 at the zoom limit
```

```
docs: update the install steps
```

Detailed:

```
perf(canvas): replace terminals with snapshots while panning

The terminal control renders in its own HWND, so every frame it had to
be repositioned and resized, which made panning and zooming stutter.
When a gesture starts, each terminal is captured and the live terminal
is hidden; it comes back 150 ms after the gesture ends.

Closes #5
```

## Issues

- Every issue belongs to a milestone (release) and has at least one `type:` and one `area:` label.
- Feature issues need an **Acceptance criteria** section.

## Pull requests

- Fill in the PR template; for changes that affect smoothness, include before/after numbers from
  `KOVAN_PERF_SELFTEST` (see [CLAUDE.md](CLAUDE.md)).
- `dotnet build -c Release` must have no warnings and `dotnet test` must pass.

## Releasing

1. Update `<Version>` in `src/KovanTerm/KovanTerm.csproj` and merge it into `main`.
2. Tag and push:
   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```
3. The `Release` workflow runs the tests, builds the self-contained `win-x64` package and creates a
   **draft** GitHub Release with the zip and a SHA256 file.
4. Review the notes on the Release page and publish it. Tags containing `-` (`v0.2.0-beta.1`) are
   marked as pre-releases.

The package doesn't need .NET installed (~60 MB zip). The executable isn't signed, so Windows
SmartScreen may warn on first launch ("More info → Run anyway").
