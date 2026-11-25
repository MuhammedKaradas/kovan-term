using System.IO;
using KovanTerm.Services;
using KovanTerm.ViewModels;
using KovanTerm.Views;

namespace KovanTerm.Tests;

/// <summary>Terminal color schemes: the catalog, choosing one per app theme, and the setting.</summary>
[Collection("TerminalThemes")]
public sealed class TerminalSchemesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public TerminalSchemesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        TerminalThemes.Select(null, null);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Every_scheme_is_complete()
    {
        Assert.Equal(TerminalThemes.All.Count, TerminalThemes.All.Select(s => s.Name).Distinct().Count());
        Assert.All(TerminalThemes.All, s => Assert.Equal(16, s.Colors.Palette.Count));
        Assert.Equal(10, TerminalThemes.DarkSchemes.Count);
        Assert.Equal(4, TerminalThemes.LightSchemes.Count);
    }

    [Fact]
    public void Legacy_console_apps_get_the_scheme_s_own_background()
    {
        // cmd and Windows PowerShell paint their text with palette entries 0 (background) and 7 (foreground)
        Assert.All(TerminalThemes.All, s => Assert.Equal(s.Colors.Background, Color(s.Colors.Palette[0])));
        Assert.All(TerminalThemes.LightSchemes, s => Assert.Equal(s.Colors.Foreground, Color(s.Colors.Palette[7])));
    }

    private static System.Windows.Media.Color Color(uint rgb) =>
        System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    [Fact]
    public void Unknown_names_fall_back_to_the_defaults()
    {
        Assert.Equal(TerminalThemes.DefaultDark, TerminalThemes.Find("no such scheme", dark: true).Name);
        Assert.Equal(TerminalThemes.DefaultLight, TerminalThemes.Find(null, dark: false).Name);
        // A light scheme isn't used for the dark theme
        Assert.Equal(TerminalThemes.DefaultDark, TerminalThemes.Find("Solarized Light", dark: true).Name);
        Assert.Equal("Nord", TerminalThemes.Find("nord", dark: true).Name);
    }

    [Fact]
    public void Selecting_changes_the_colors_in_use()
    {
        TerminalThemes.Select(null, null);
        Assert.True(TerminalThemes.Select("Dracula", "GitHub Light"));
        Assert.Same(TerminalThemes.Find("Dracula", true).Colors, TerminalThemes.For(dark: true));
        Assert.Same(TerminalThemes.Find("GitHub Light", false).Colors, TerminalThemes.For(dark: false));
        Assert.False(TerminalThemes.Select("Dracula", "GitHub Light"));
    }

    [Fact]
    public void The_setting_is_saved_by_name()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var vm = new SettingsViewModel(store);
        Assert.Equal(TerminalThemes.DefaultDark, vm.TerminalSchemeDark);
        Assert.Contains("Tokyo Night", vm.DarkSchemes);

        vm.TerminalSchemeDark = "Tokyo Night";
        vm.TerminalSchemeLight = "Catppuccin Latte";

        Assert.Equal("Tokyo Night", store.Load().TerminalSchemeDark);
        Assert.Equal("Catppuccin Latte", store.Load().TerminalSchemeLight);
    }
}
