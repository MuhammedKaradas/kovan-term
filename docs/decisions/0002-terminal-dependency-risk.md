# 0002: Risk of the terminal control dependency

- **Status:** Accepted
- **Date:** 2026-10-20
- **Related:** #8, [0001](0001-dotnet-wpf-native-terminal.md)

## Context

The terminal layer depends on two community packages:

| Package | Who | Release history | Notes |
|---|---|---|---|
| `EasyWindowsTerminalControl` | Single maintainer ([mitchcapper](https://github.com/mitchcapper/EasyWindowsTerminalControl)) | 5 releases; latest 1.0.38 (2026-07-16) | MIT (license file added 2026-07-15), 37 stars, 1 open issue. The latest release moved to Microsoft's official `Microsoft.Windows.Console.ConPTY` package |
| `CI.Microsoft.Terminal.Wpf` | "CI2Nuget Repackage Team" | 2 releases; latest 1.25.260303002 (2026-07-16) | An **unofficial** repackage of Windows Terminal's WPF control |

Microsoft still doesn't publish the WPF terminal control as an official package:
[microsoft/terminal#6999](https://github.com/microsoft/terminal/issues/6999) ("Productize the WPF, UWP
Terminal Controls") has been open since 2020. The control's source is in the Windows Terminal repo
under the MIT license.

### Risks

1. **Abandonment:** both packages are backed by one person or a small team. Updates may stop.
2. **API changes:** the underlying control isn't official; its interface may change in new versions.
3. **Windows compatibility:** if a new Windows version breaks the control, a fix may take a while.

These are not risks of **immediate breakage**: as long as versions are pinned, the existing binaries
keep working. The real risk is having to wait when a bug or security fix is needed later.

## Measuring the fallback

The fallback (option 3 in 0001): xterm.js inside WebView2 for each terminal. Measured on the same
machine, excluding shell processes (UI cost only):

| | 4 terminals | 10 terminals |
|---|---|---|
| **Native control (KovanTerm, Release)** | 190 MB working set / 177 MB private | 282 MB / 291 MB, 11 processes |
| WebView2 + xterm.js (shared environment) | 626 MB / 326 MB, 10 processes | 1,029 MB / 530 MB, 16 processes |

- Measured 15 s after startup: the app plus all child processes, excluding shells such as `cmd.exe`.
- The xterm.js instances in the WebView2 prototype were **not connected** to a shell; real-world numbers
  would be higher. So this comparison is optimistic for WebView2.
- The native control is ~3.5× lighter in working set and ~1.8× lighter in private memory.

## Decision

We stay with the native control and manage the risk as follows:

1. **Pin versions:** package versions are pinned exactly in the `csproj`; updates are deliberate, and
   the smoothness/cleanup checks (snapshot mode, process termination) are repeated by hand.
2. **Build-from-source fallback:** if the repackaging team disappears, the `Microsoft.Terminal.Wpf`
   control can be built from the Windows Terminal source (`src/cascadia/WpfTerminalControl`, MIT).
   `EasyWindowsTerminalControl` is MIT too and can be vendored and maintained if needed.
3. **Abstract the terminal control:** the code that touches the terminal already lives in the view
   layer, mostly in `TerminalCanvasController`. Putting the control behind an interface, so another
   implementation can be swapped in, is planned as a separate item (#30). That would keep a move to
   WebView2 down to days if it's ever needed.
4. **Triggers:** this decision is revisited if any of the following happens:
   - The packages go more than 12 months without an update **and** a critical unresolved bug appears
   - Microsoft publishes an official WPF/WinUI terminal control (microsoft/terminal#6999 is closed)
   - The control stops working on a supported Windows version

## Consequences

- The memory and smoothness advantage is kept. It's the reason this project exists.
- Package updates come with a manual testing cost.
- Until the abstraction is done, the cost of switching stays high.
