using System.Windows.Media;
using KovanTerm.Terminals;

namespace KovanTerm.Views;

/// <summary>A terminal color scheme the user can pick (Settings → Appearance).</summary>
public sealed record NamedScheme(string Name, bool IsDark, TerminalColorScheme Colors);

/// <summary>
/// Terminal color schemes (#146): one is in use for the app's dark theme and one for its light theme, chosen in
/// Settings. The defaults are Campbell (Windows Terminal's) and a One Half Light variant.
/// </summary>
public static class TerminalThemes
{
    public const string DefaultDark = "Campbell";
    public const string DefaultLight = "One Half Light";

    private static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>
    /// Legacy console apps (cmd, Windows PowerShell) paint "default" text with explicit palette entries: background =
    /// 0 (black), foreground = 7 (white). Entry 0 is therefore always the scheme's background, or their terminals would
    /// show another shade than the rest (as Campbell does: its black is its background). In a light scheme, 7 is the
    /// dark foreground too; 8 and 15 follow the same idea.
    /// </summary>
    private static NamedScheme S(string name, bool dark, uint background, uint foreground, uint selection, uint[] palette)
    {
        palette[0] = background;
        return new(name, dark, new TerminalColorScheme(Rgb(background), Rgb(foreground), Rgb(selection), palette));
    }
    public static IReadOnlyList<NamedScheme> All { get; } =
    [
        S(DefaultDark, true, 0x0C0C0C, 0xE6E8F0, 0x3A3C4A,
            [0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
             0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2]),
        S("Catppuccin Mocha", true, 0x1E1E2E, 0xCDD6F4, 0x585B70,
            [0x45475A, 0xF38BA8, 0xA6E3A1, 0xF9E2AF, 0x89B4FA, 0xF5C2E7, 0x94E2D5, 0xBAC2DE,
             0x585B70, 0xF38BA8, 0xA6E3A1, 0xF9E2AF, 0x89B4FA, 0xF5C2E7, 0x94E2D5, 0xA6ADC8]),
        S("Dracula", true, 0x282A36, 0xF8F8F2, 0x44475A,
            [0x21222C, 0xFF5555, 0x50FA7B, 0xF1FA8C, 0xBD93F9, 0xFF79C6, 0x8BE9FD, 0xF8F8F2,
             0x6272A4, 0xFF6E6E, 0x69FF94, 0xFFFFA5, 0xD6ACFF, 0xFF92DF, 0xA4FFFF, 0xFFFFFF]),
        S("Gruvbox Dark", true, 0x282828, 0xEBDBB2, 0x504945,
            [0x282828, 0xCC241D, 0x98971A, 0xD79921, 0x458588, 0xB16286, 0x689D6A, 0xA89984,
             0x928374, 0xFB4934, 0xB8BB26, 0xFABD2F, 0x83A598, 0xD3869B, 0x8EC07C, 0xEBDBB2]),
        S("Monokai", true, 0x272822, 0xF8F8F2, 0x49483E,
            [0x272822, 0xF92672, 0xA6E22E, 0xF4BF75, 0x66D9EF, 0xAE81FF, 0xA1EFE4, 0xF8F8F2,
             0x75715E, 0xF92672, 0xA6E22E, 0xF4BF75, 0x66D9EF, 0xAE81FF, 0xA1EFE4, 0xF9F8F5]),
        S("Nord", true, 0x2E3440, 0xD8DEE9, 0x434C5E,
            [0x3B4252, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x88C0D0, 0xE5E9F0,
             0x4C566A, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x8FBCBB, 0xECEFF4]),
        S("One Half Dark", true, 0x282C34, 0xDCDFE4, 0x474E5D,
            [0x282C34, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xDCDFE4,
             0x5A6374, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xDCDFE4]),
        S("Rosé Pine", true, 0x191724, 0xE0DEF4, 0x403D52,
            [0x26233A, 0xEB6F92, 0x31748F, 0xF6C177, 0x9CCFD8, 0xC4A7E7, 0xEBBCBA, 0xE0DEF4,
             0x6E6A86, 0xEB6F92, 0x31748F, 0xF6C177, 0x9CCFD8, 0xC4A7E7, 0xEBBCBA, 0xE0DEF4]),
        S("Solarized Dark", true, 0x002B36, 0x839496, 0x073642,
            [0x073642, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0xEEE8D5,
             0x586E75, 0xCB4B16, 0x586E75, 0x657B83, 0x839496, 0x6C71C4, 0x93A1A1, 0xFDF6E3]),
        S("Tokyo Night", true, 0x1A1B26, 0xC0CAF5, 0x33467C,
            [0x15161E, 0xF7768E, 0x9ECE6A, 0xE0AF68, 0x7AA2F7, 0xBB9AF7, 0x7DCFFF, 0xA9B1D6,
             0x414868, 0xF7768E, 0x9ECE6A, 0xE0AF68, 0x7AA2F7, 0xBB9AF7, 0x7DCFFF, 0xC0CAF5]),

        S(DefaultLight, false, 0xFAFAFA, 0x383A42, 0xD0D4DC,
            [0xFAFAFA, 0xE45649, 0x50A14F, 0xC18301, 0x0184BC, 0xA626A4, 0x0997B3, 0x383A42,
             0xA0A1A7, 0xDF6C75, 0x50A14F, 0xC18301, 0x0184BC, 0xA626A4, 0x0997B3, 0x090A0B]),
        S("Catppuccin Latte", false, 0xEFF1F5, 0x4C4F69, 0xACB0BE,
            [0xEFF1F5, 0xD20F39, 0x40A02B, 0xDF8E1D, 0x1E66F5, 0xEA76CB, 0x179299, 0x4C4F69,
             0x9CA0B0, 0xD20F39, 0x40A02B, 0xDF8E1D, 0x1E66F5, 0xEA76CB, 0x179299, 0x232634]),
        S("GitHub Light", false, 0xFFFFFF, 0x24292F, 0xB6E3FF,
            [0xFFFFFF, 0xCF222E, 0x116329, 0x4D2D00, 0x0969DA, 0x8250DF, 0x1B7C83, 0x24292F,
             0x6E7781, 0xA40E26, 0x1A7F37, 0x633C01, 0x218BFF, 0xA475F9, 0x3192AA, 0x0E1116]),
        S("Solarized Light", false, 0xFDF6E3, 0x586E75, 0xEEE8D5,
            [0xFDF6E3, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0x586E75,
             0x93A1A1, 0xCB4B16, 0x859900, 0xB58900, 0x268BD2, 0x6C71C4, 0x2AA198, 0x073642]),
    ];

    public static IReadOnlyList<NamedScheme> DarkSchemes { get; } = All.Where(s => s.IsDark).ToList();
    public static IReadOnlyList<NamedScheme> LightSchemes { get; } = All.Where(s => !s.IsDark).ToList();

    private static NamedScheme _dark = Find(DefaultDark, dark: true);
    private static NamedScheme _light = Find(DefaultLight, dark: false);

    /// <summary>A scheme by name, for that theme; the default one if the name is unknown (e.g. typed by hand).</summary>
    public static NamedScheme Find(string? name, bool dark)
    {
        var list = dark ? DarkSchemes : LightSchemes;
        return list.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? list[0];
    }

    /// <summary>The schemes in use (from settings); <c>true</c> if they changed.</summary>
    public static bool Select(string? darkName, string? lightName)
    {
        var (dark, light) = (Find(darkName, dark: true), Find(lightName, dark: false));
        if (dark == _dark && light == _light) return false;
        (_dark, _light) = (dark, light);
        return true;
    }

    public static TerminalColorScheme For(bool dark) => (dark ? _dark : _light).Colors;

    public static Color BackgroundFor(bool dark) => For(dark).Background;
}
