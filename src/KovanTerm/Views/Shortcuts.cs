using System.Windows.Input;

namespace KovanTerm.Views;

public enum ShortcutAction
{
    NextTerminal,
    PreviousTerminal,
    FitAll,
    ZoomToFocused,
    NewTerminal,
    ToggleAgentsPanel,
    ToggleChangesPanel,
    RecentConversations,
    ToggleBoard,
    NewNote,
    NextWorkspace,
    PreviousWorkspace,
    Settings,
    Statistics,
    Broadcast,
    Dictate,
    VoiceCommand,
    Find,
    ClearScrollback,
    Tools,
}

/// <summary>
/// App shortcuts. Since they must work while a terminal is focused, keys commonly used by terminal programs
/// (e.g. Ctrl+T: transpose characters in bash) are avoided; most use Ctrl+Shift.
/// Keep this list in sync with <c>docs/shortcuts.md</c>.
/// </summary>
public static class Shortcuts
{
    public static IReadOnlyList<(Key Key, ModifierKeys Modifiers, ShortcutAction Action, string Description)> All { get; } =
    [
        (Key.Tab, ModifierKeys.Control, ShortcutAction.NextTerminal, "Go to next terminal"),
        (Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.PreviousTerminal, "Go to previous terminal"),
        (Key.F, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.FitAll, "Fit all terminals"),
        (Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ZoomToFocused, "Zoom to focused terminal"),
        (Key.T, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.NewTerminal, "New terminal"),
        (Key.A, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ToggleAgentsPanel, "Show/hide the Agents panel"),
        (Key.G, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ToggleChangesPanel, "Show/hide the Changes panel"),
        (Key.O, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.RecentConversations, "Reopen a recent Claude Code conversation"),
        (Key.B, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ToggleBoard, "Show/hide the agents' Board"),
        (Key.N, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.NewNote, "New note"),
        // Not Ctrl+Alt+digit: that's AltGr on many keyboard layouts, which types characters
        (Key.PageDown, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.NextWorkspace, "Show the next workspace"),
        (Key.PageUp, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.PreviousWorkspace, "Show the previous workspace"),
        (Key.OemComma, ModifierKeys.Control, ShortcutAction.Settings, "Settings"),
        (Key.Y, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.Statistics, "Statistics: time and tokens by day, project and model"),
        (Key.S, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.Broadcast, "Send a prompt to several agents"),
        (Key.Space, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.Dictate, "Dictate into the current terminal (press again to stop)"),
        (Key.K, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.VoiceCommand, "Voice command: say what to do (press again to stop)"),
        // Not Ctrl+F (forward a character in bash, a page in less and vim) or Ctrl+Shift+F (fit all)
        (Key.H, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.Find, "Find in the current terminal"),
        (Key.L, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ClearScrollback, "Clear the current terminal's scrollback"),
        (Key.U, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.Tools, "Tools: the agents' MCP servers, plugins, skills, subagents, commands and hooks"),
    ];

    public static ShortcutAction? Match(Key key, ModifierKeys modifiers)
    {
        // Don't interfere with Windows logo key shortcuts
        modifiers &= ~ModifierKeys.Windows;
        foreach (var s in All)
            if (s.Key == key && s.Modifiers == modifiers)
                return s.Action;
        return null;
    }
}
