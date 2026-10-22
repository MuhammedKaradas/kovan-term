using System.Windows;
using System.Windows.Input;
using KovanTerm.ViewModels;
using KovanTerm.Views;

namespace KovanTerm.Tests;

public sealed class NavigationAndShortcutTests
{
    private sealed record N(string Name, Rect Bounds);

    private static N Node(string name, double x, double y) => new(name, new Rect(x, y, 560, 374));

    // Grid: top row slightly uneven (y 40 and 90), bottom row at y 500
    private static readonly N A = Node("A", 640, 90), B = Node("B", 40, 40), C = Node("C", 1240, 60),
                              D = Node("D", 640, 500), E = Node("E", 40, 520);
    private static readonly N[] All = [A, B, C, D, E];

    [Fact]
    public void ReadingOrder_RowByRowLeftToRight()
    {
        var order = NodeNavigation.ReadingOrder(All, n => n.Bounds).Select(n => n.Name);
        Assert.Equal(["B", "A", "C", "E", "D"], order);
    }

    [Fact]
    public void Next_ForwardAndBackward_WrapsAround()
    {
        Assert.Same(A, NodeNavigation.Next(All, n => n.Bounds, B, forward: true, default));
        Assert.Same(B, NodeNavigation.Next(All, n => n.Bounds, D, forward: true, default));
        Assert.Same(D, NodeNavigation.Next(All, n => n.Bounds, B, forward: false, default));
    }

    [Fact]
    public void Next_NoFocusedNode_StartsNearestToCenter()
    {
        var center = new Point(900, 700); // near D's center
        Assert.Same(D, NodeNavigation.Next(All, n => n.Bounds, null, forward: true, center));
    }

    [Fact]
    public void Next_EmptyList_ReturnsNull()
    {
        Assert.Null(NodeNavigation.Next(Array.Empty<N>(), n => n.Bounds, null, forward: true, default));
    }

    [Theory]
    [InlineData(Key.Tab, ModifierKeys.Control, ShortcutAction.NextTerminal)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.PreviousTerminal)]
    [InlineData(Key.F, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.FitAll)]
    [InlineData(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ZoomToFocused)]
    [InlineData(Key.T, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.NewTerminal)]
    [InlineData(Key.T, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows, ShortcutAction.NewTerminal)]
    public void Match_MatchesShortcut(Key key, ModifierKeys modifiers, ShortcutAction expected)
    {
        Assert.Equal(expected, Shortcuts.Match(key, modifiers));
    }

    [Theory]
    [InlineData(Key.T, ModifierKeys.Control)]       // bash: transpose characters
    [InlineData(Key.Tab, ModifierKeys.None)]        // shell completion
    [InlineData(Key.F, ModifierKeys.Control)]       // readline: move forward
    [InlineData(Key.C, ModifierKeys.Control)]       // interrupt signal
    [InlineData(Key.Enter, ModifierKeys.None)]
    public void Match_IgnoresKeysUsedByTerminal(Key key, ModifierKeys modifiers)
    {
        Assert.Null(Shortcuts.Match(key, modifiers));
    }

    [Fact]
    public void Shortcuts_DoNotConflict()
    {
        var keys = Shortcuts.All.Select(s => (s.Key, s.Modifiers)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
