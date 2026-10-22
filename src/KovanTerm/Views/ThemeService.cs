using System.Windows;
using System.Windows.Media;
using KovanTerm.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

/// <summary>
/// Applies the light/dark theme (WPF-UI, Mica backdrop) and tells the terminal layer which one is active,
/// since terminal colors aren't WPF resources.
/// </summary>
public sealed class ThemeService
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    /// <summary>Resource key for the terminal background, used by non-terminal node bodies (e.g. the error view).</summary>
    public const string TerminalBackgroundBrushKey = "TerminalBackgroundBrush";

    private readonly Window _window;
    private bool _watching;

    public bool IsDark { get; private set; } = true;

    /// <summary>Raised when the effective theme switches between dark and light.</summary>
    public event Action<bool>? IsDarkChanged;

    public ThemeService(Window window)
    {
        _window = window;
        // Fires both for our own Apply calls and for system changes picked up by SystemThemeWatcher
        ApplicationThemeManager.Changed += (theme, _) => SetIsDark(theme != ApplicationTheme.Light);
    }

    public void Apply(ThemePreference preference)
    {
        if (preference == ThemePreference.System)
        {
            if (!_watching)
            {
                SystemThemeWatcher.Watch(_window, Backdrop, updateAccents: true);
                _watching = true;
            }
            ApplicationThemeManager.Apply(SystemIsLight() ? ApplicationTheme.Light : ApplicationTheme.Dark, Backdrop);
            return;
        }

        if (_watching)
        {
            SystemThemeWatcher.UnWatch(_window);
            _watching = false;
        }
        ApplicationThemeManager.Apply(preference == ThemePreference.Light ? ApplicationTheme.Light : ApplicationTheme.Dark, Backdrop);
    }

    private static bool SystemIsLight() => ApplicationThemeManager.GetSystemTheme() is
        SystemTheme.Light or SystemTheme.HCWhite or SystemTheme.Sunrise or SystemTheme.Flow;

    private void SetIsDark(bool dark)
    {
        Application.Current.Resources[TerminalBackgroundBrushKey] = new SolidColorBrush(TerminalThemes.BackgroundFor(dark));
        if (dark == IsDark) return;
        IsDark = dark;
        IsDarkChanged?.Invoke(dark);
    }
}
