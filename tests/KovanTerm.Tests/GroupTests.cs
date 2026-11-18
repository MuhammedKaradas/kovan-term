using System.Windows;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class GroupTests
{
    private static TerminalNodeViewModel Node(double x, double y, string? folder = null) => new()
    {
        Title = "t", CommandLine = "cmd.exe", Location = new Point(x, y), Width = 500, Height = 300, WorkingDirectory = folder,
    };

    [Fact]
    public void CreateGroup_FramesTheMembersWithRoomForTheHeader()
    {
        var a = Node(100, 100);
        var b = Node(700, 200);

        var group = MainViewModel.CreateGroup(new Point(0, 0), [a, b]);

        Assert.True(group.IsGroup);
        Assert.False(group.IsTerminal);
        var frame = group.Bounds;
        Assert.True(frame.Contains(a.Bounds) && frame.Contains(b.Bounds));
        // The header strip above the topmost member stays free
        Assert.True(a.Bounds.Top - frame.Top >= 60);
    }

    [Fact]
    public void CreateGroup_NamesItAfterACommonFolder()
    {
        Assert.Equal("api", MainViewModel.CreateGroup(default, [Node(0, 0, @"C:\code\api"), Node(600, 0, @"C:\code\api")]).Title);
        Assert.Equal("Group", MainViewModel.CreateGroup(default, [Node(0, 0, @"C:\code\api"), Node(600, 0, @"C:\code\web")]).Title);
        Assert.Equal("Group", MainViewModel.CreateGroup(new Point(40, 50), []).Title);
    }

    [Fact]
    public void EmptyGroup_StartsAtTheGivenPlace()
    {
        var group = MainViewModel.CreateGroup(new Point(40, 50), []);

        Assert.Equal(new Rect(40, 50, 1000, 640), group.Bounds);
    }

    [Fact]
    public void Group_RoundTripsThroughState_WithItsFullSize()
    {
        var group = MainViewModel.CreateGroup(new Point(0, 0), []);
        group.Title = "Backend";
        group.GroupSize = new Size(6000, 4500); // bigger than a terminal may be

        var restored = TerminalNodeViewModel.FromState(group.ToState());

        Assert.True(restored.IsGroup);
        Assert.Equal("Backend", restored.Title);
        Assert.Equal(new Size(6000, 4500), restored.GroupSize);
    }
}
