using System.Windows.Media;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;

namespace KovanTerm.Views;

/// <summary>Terminal color schemes for the app's dark and light themes.</summary>
public static class TerminalThemes
{
    public static readonly Color DarkBackground = Color.FromRgb(0x0c, 0x0c, 0x0c);
    public static readonly Color LightBackground = Color.FromRgb(0xfa, 0xfa, 0xfa);

    public static readonly TerminalTheme Dark = new()
    {
        DefaultBackground = EasyTerminalControl.ColorToVal(DarkBackground),
        DefaultForeground = EasyTerminalControl.ColorToVal(Color.FromRgb(0xe6, 0xe8, 0xf0)),
        DefaultSelectionBackground = EasyTerminalControl.ColorToVal(Color.FromRgb(0x3a, 0x3c, 0x4a)),
        CursorStyle = CursorStyle.BlinkingBar,
        // Campbell (Windows Terminal default)
        ColorTable = Palette(0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
                             0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2),
    };

    public static readonly TerminalTheme Light = new()
    {
        DefaultBackground = EasyTerminalControl.ColorToVal(LightBackground),
        DefaultForeground = EasyTerminalControl.ColorToVal(Color.FromRgb(0x38, 0x3a, 0x42)),
        DefaultSelectionBackground = EasyTerminalControl.ColorToVal(Color.FromRgb(0xd0, 0xd4, 0xdc)),
        CursorStyle = CursorStyle.BlinkingBar,
        // Based on One Half Light. Legacy console apps (cmd, Windows PowerShell) paint "default" text with
        // explicit palette entries: background = 0 (black), foreground = 7 (white). So in a light scheme,
        // entry 0 is the light background and 7 the dark foreground; 8/15 follow the same idea.
        ColorTable = Palette(0xFAFAFA, 0xE45649, 0x50A14F, 0xC18301, 0x0184BC, 0xA626A4, 0x0997B3, 0x383A42,
                             0xA0A1A7, 0xDF6C75, 0x50A14F, 0xC18301, 0x0184BC, 0xA626A4, 0x0997B3, 0x090A0B),
    };

    public static TerminalTheme For(bool dark) => dark ? Dark : Light;

    public static Color BackgroundFor(bool dark) => dark ? DarkBackground : LightBackground;

    /// <summary>Converts 0xRRGGBB colors to the COLORREF (0x00BBGGRR) values the terminal expects.</summary>
    private static uint[] Palette(params uint[] rgb) =>
        rgb.Select(c => ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF)).ToArray();
}
