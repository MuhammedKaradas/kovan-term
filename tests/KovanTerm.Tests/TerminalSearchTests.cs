using KovanTerm.Interop;
using KovanTerm.Views;
using System.Windows.Input;

namespace KovanTerm.Tests;

/// <summary>Find in a terminal: which match is shown, and the shortcuts.</summary>
public sealed class TerminalSearchTests
{
    [Theory]
    // A new search shows the newest match (the bottom of the buffer)
    [InlineData(0, 5, false, false, 5)]
    [InlineData(3, 5, false, true, 5)]
    // Again: one older, wrapping from the oldest to the newest
    [InlineData(5, 5, true, false, 4)]
    [InlineData(1, 5, true, false, 5)]
    // Newer, wrapping from the newest to the oldest
    [InlineData(4, 5, true, true, 5)]
    [InlineData(5, 5, true, true, 1)]
    // The buffer shrank under the current match: start from the newest again
    [InlineData(9, 5, true, false, 5)]
    [InlineData(1, 1, true, false, 1)]
    public void Steps_through_matches(int current, int count, bool again, bool newer, int expected) =>
        Assert.Equal(expected, TerminalSearch.Step(current, count, again, newer));

    [Fact]
    public void Status_text()
    {
        Assert.Equal("No results", SearchResult.None.Text);
        Assert.Equal("2 of 5", new SearchResult(2, 5).Text);
        Assert.Equal("3 of 500+", new SearchResult(3, TerminalSearch.MaxMatches).Text);
    }

    [Fact]
    public void Shortcuts_dont_take_keys_terminal_programs_use()
    {
        Assert.Equal(ShortcutAction.Find, Shortcuts.Match(Key.H, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(ShortcutAction.ClearScrollback, Shortcuts.Match(Key.L, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Null(Shortcuts.Match(Key.F, ModifierKeys.Control));
        Assert.Null(Shortcuts.Match(Key.L, ModifierKeys.Control));
        Assert.Equal(Shortcuts.All.Count, Shortcuts.All.Select(s => (s.Key, s.Modifiers)).Distinct().Count());
    }
}
