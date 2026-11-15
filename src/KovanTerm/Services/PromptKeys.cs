using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>
/// Recognizes keys that answer an agent's prompt (Enter, Escape, a digit) in what the user types into a terminal.
/// Needed because refusing a permission with Escape sends no hook: without this, the node would keep showing
/// "Needs you" after the user answered in the terminal itself.
/// </summary>
public static partial class PromptKeys
{
    // win32-input-mode key event: ESC [ Vk ; Sc ; Uc ; Kd ; Cs ; Rc _  (Uc = character, Kd = 1 on key down)
    [GeneratedRegex(@"\x1b\[\d*;\d*;(\d+);1;\d*;\d*_")]
    private static partial Regex Win32KeyDown();

    public static bool IsAnswer(ReadOnlySpan<char> input)
    {
        // A lone Escape; longer sequences starting with it are arrow keys etc.
        if (input is ['\x1b']) return true;
        if (input is [>= '1' and <= '9']) return true;
        if (input.Contains('\r')) return true;
        foreach (var m in Win32KeyDown().EnumerateMatches(input))
        {
            var match = input.Slice(m.Index, m.Length);
            var fields = match[2..^1].ToString().Split(';');
            if (int.TryParse(fields[2], out var c) && (c is 13 or 27 || (c >= '1' && c <= '9')))
                return true;
        }
        return false;
    }
}
