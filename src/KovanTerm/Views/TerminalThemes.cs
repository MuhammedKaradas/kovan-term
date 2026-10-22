using System.Windows.Media;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;

namespace KovanTerm.Views;

public static class TerminalThemes
{
    public static readonly Color DarkBackground = Color.FromRgb(0x0c, 0x0c, 0x0c);

    public static readonly TerminalTheme Dark = new()
    {
        DefaultBackground = EasyTerminalControl.ColorToVal(DarkBackground),
        DefaultForeground = EasyTerminalControl.ColorToVal(Color.FromRgb(0xe6, 0xe8, 0xf0)),
        DefaultSelectionBackground = EasyTerminalControl.ColorToVal(Color.FromRgb(0x3a, 0x3c, 0x4a)),
        CursorStyle = CursorStyle.BlinkingBar,
        // Campbell colors (Windows Terminal default)
        ColorTable = new uint[] { 0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC,
                                  0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2 },
    };
}
