using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;

namespace KovanTerm.Terminals.WindowsTerminal;

/// <summary>
/// <see cref="ITerminalSurface"/> on EasyWindowsTerminalControl: Windows Terminal's native control (C++/DirectX in an
/// HwndHost) over ConPTY. This folder is the only place that uses the package's types.
/// </summary>
public sealed class WindowsTerminalSurface : ITerminalSurface
{
    private readonly EasyTerminalControl _control = new() { FontSizeWhenSettingTheme = 13 };
    private bool _hooked;

    public FrameworkElement View => _control;

    public event Action<string>? Output;
    public event TerminalInputHandler? Input;

    public WindowsTerminalSurface()
    {
        // The pseudo-console is created when the control loads
        _control.Loaded += (_, _) => Hook();
    }

    public void Start(string commandLine, string? directory)
    {
        // The control starts the process when it loads, with these
        _control.StartupCommandLine = commandLine;
        if (directory != null) _control.WorkingDirectory = directory;
    }

    private void Hook()
    {
        if (_hooked || _control.ConPTYTerm is not { } pty) return;
        _hooked = true;
        pty.TerminalOutput += (_, e) => Output?.Invoke(e.Data);
        pty.InterceptInputToTermApp += (ref Span<char> input) => Input?.Invoke(input);
    }

    public void Write(string text) => _control.ConPTYTerm?.WriteToTerm(text);

    public void SetAppearance(TerminalColorScheme colors, string fontFamily, int fontSize) =>
        _control.Terminal?.SetTheme(ThemeFor(colors), fontFamily, (short)fontSize);

    // The control is the TerminalControl containing the HwndHost; focusing it calls SetFocus on the HWND
    public bool Focus() => _control.Terminal?.Focus() == true;

    public void Terminate()
    {
        var pty = _control.ConPTYTerm;
        if (pty == null) return;
        try
        {
            if (pty.Process is { HasExited: false } process)
                process.Kill(EntireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // The process may have exited on its own in the meantime
            Debug.WriteLine($"Couldn't kill terminal process: {ex.Message}");
        }

        // The library doesn't close the pseudo-console when the process ends, leaving conhost.exe running.
        // ClosePseudoConsole can block, so it isn't called on the UI thread.
        Task.Run(() =>
        {
            try { ((ITerminalConnection)pty).Close(); }
            catch (Exception ex) { Debug.WriteLine($"Couldn't close pseudo-console: {ex.Message}"); }
        });
    }

    private static readonly Dictionary<TerminalColorScheme, TerminalTheme> Themes = [];

    private static TerminalTheme ThemeFor(TerminalColorScheme colors)
    {
        if (Themes.TryGetValue(colors, out var theme)) return theme;
        theme = new TerminalTheme
        {
            DefaultBackground = EasyTerminalControl.ColorToVal(colors.Background),
            DefaultForeground = EasyTerminalControl.ColorToVal(colors.Foreground),
            DefaultSelectionBackground = EasyTerminalControl.ColorToVal(colors.SelectionBackground),
            CursorStyle = CursorStyle.BlinkingBar,
            ColorTable = colors.Palette.Select(ColorRef).ToArray(),
        };
        Themes[colors] = theme;
        return theme;
    }

    /// <summary>0xRRGGBB to the COLORREF (0x00BBGGRR) the control expects.</summary>
    private static uint ColorRef(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
}
