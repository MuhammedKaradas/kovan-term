using System.Windows.Input;

namespace KovanTerm.Views;

public enum ShortcutAction
{
    NextTerminal,
    PreviousTerminal,
    FitAll,
    ZoomToFocused,
    NewTerminal,
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
