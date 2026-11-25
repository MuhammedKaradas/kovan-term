using System.Windows;

namespace KovanTerm.Terminals;

/// <summary>Keys the user typed, before they reach the program.</summary>
public delegate void TerminalInputHandler(ReadOnlySpan<char> input);

/// <summary>
/// A live terminal: a pseudo-console running a command, and the control that renders it. The rest of the app only
/// knows this interface, so the terminal control can be replaced (decision record 0002). The one implementation is in
/// <c>Terminals/WindowsTerminal</c>.
/// <para>
/// The view is expected to render into its own HWND (an <see cref="System.Windows.Interop.HwndHost"/> somewhere inside
/// it): it's what <see cref="Interop.TerminalSnapshot"/> captures, and why the canvas hides it while it moves.
/// </para>
/// </summary>
public interface ITerminalSurface
{
    /// <summary>The control to put in the node body.</summary>
    FrameworkElement View { get; }

    /// <summary>Runs <paramref name="commandLine"/> in <paramref name="directory"/>. Called once, before <see cref="View"/> is shown.</summary>
    void Start(string commandLine, string? directory);

    /// <summary>The program printed something (raw, with escape sequences). Raised on a background thread.</summary>
    event Action<string>? Output;

    /// <summary>The user typed something. Raised on a background thread, before the program gets it.</summary>
    event TerminalInputHandler? Input;

    /// <summary>Sends text to the program as if the user had typed it.</summary>
    void Write(string text);

    /// <summary>Clears the scrollback (lines above the screen); the screen itself is left to the program (#141).</summary>
    void ClearScrollback();

    void SetAppearance(TerminalColorScheme colors, string fontFamily, int fontSize);

    /// <summary>Gives the terminal keyboard focus; <c>false</c> if it can't take it yet.</summary>
    bool Focus();

    /// <summary>Kills the process and every process under it (e.g. cmd → claude → MCP servers), and closes the pseudo-console.</summary>
    void Terminate();
}

/// <param name="Palette">The 16 ANSI colors as 0xRRGGBB.</param>
public sealed record TerminalColorScheme(System.Windows.Media.Color Background, System.Windows.Media.Color Foreground,
    System.Windows.Media.Color SelectionBackground, IReadOnlyList<uint> Palette);
