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
/// Uygulama kısayolları. Terminal odaktayken de çalışmaları gerektiği için terminal programlarının
/// sık kullandığı tuşlardan (ör. Ctrl+T: bash'te karakter yer değiştirme) kaçınılır; çoğu Ctrl+Shift ile.
/// Liste <c>docs/kisayollar.md</c> ile aynı tutulmalı.
/// </summary>
public static class Shortcuts
{
    public static IReadOnlyList<(Key Key, ModifierKeys Modifiers, ShortcutAction Action, string Description)> All { get; } =
    [
        (Key.Tab, ModifierKeys.Control, ShortcutAction.NextTerminal, "Sonraki terminale geç"),
        (Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.PreviousTerminal, "Önceki terminale geç"),
        (Key.F, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.FitAll, "Tüm terminalleri sığdır"),
        (Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ZoomToFocused, "Odaktaki terminale yakınlaştır"),
        (Key.T, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.NewTerminal, "Yeni terminal"),
    ];

    public static ShortcutAction? Match(Key key, ModifierKeys modifiers)
    {
        // Windows logo tuşu kısayollarına karışma
        modifiers &= ~ModifierKeys.Windows;
        foreach (var s in All)
            if (s.Key == key && s.Modifiers == modifiers)
                return s.Action;
        return null;
    }
}
