using KovanTerm.Services;
using KovanTerm.ViewModels;
using KovanTerm.Views;

namespace KovanTerm.Tests;

/// <summary>A color per project: projects stay apart, keep their color, and only terminals with a folder have one.</summary>
public sealed class ProjectColorsTests
{
    [Fact]
    public void New_projects_get_the_least_used_colors()
    {
        var map = new ProjectColorMap(8);
        var colors = Enumerable.Range(0, 8).Select(i => map.Assign($@"c:\work\p{i}", out _)).ToList();

        Assert.Equal(8, colors.Distinct().Count());
        // The ninth shares a color, but only once all are used
        Assert.InRange(map.Assign(@"c:\work\p8", out var added), 0, 7);
        Assert.True(added);
    }

    [Fact]
    public void A_project_keeps_its_color()
    {
        var map = new ProjectColorMap(8);
        var first = map.Assign(@"c:\work\shop", out _);
        Assert.Equal(first, map.Assign(@"C:\Work\Shop", out var added));
        Assert.False(added);

        // Saved and loaded again (next run)
        var again = new ProjectColorMap(8, new Dictionary<string, int>(map.Colors));
        Assert.Equal(first, again.Find(@"c:\work\shop"));
    }

    [Fact]
    public void The_first_project_gets_its_hashed_color_and_bad_saved_values_are_dropped()
    {
        var map = new ProjectColorMap(8, new Dictionary<string, int> { ["x"] = 99, ["y"] = -1 });
        Assert.Empty(map.Colors);
        Assert.Equal(ProjectColorMap.Hash("c:/a") % 8, map.Assign("c:/a", out _));
        Assert.Equal(ProjectColorMap.Hash("C:/A"), ProjectColorMap.Hash("c:/a"));
    }

    [Fact]
    public void Old_projects_are_forgotten_past_the_limit()
    {
        var map = new ProjectColorMap(8);
        for (var i = 0; i <= ProjectColorMap.MaxProjects; i++) map.Assign($"p{i}", out _);

        Assert.Equal(ProjectColorMap.MaxProjects, map.Colors.Count);
        Assert.Null(map.Find("p0"));
    }

    [Fact]
    public void A_terminal_s_project_is_its_folder_and_notes_have_none()
    {
        var a = new TerminalNodeViewModel { Title = "a", CommandLine = "claude", WorkingDirectory = @"C:\Work\Shop\" };
        var b = new TerminalNodeViewModel { Title = "b", CommandLine = "codex", WorkingDirectory = @"c:\work\shop" };
        Assert.Equal(@"c:\work\shop", a.ProjectKey);
        Assert.Equal(a.ProjectKey, b.ProjectKey);

        Assert.Null(new TerminalNodeViewModel { Title = "n", CommandLine = "", IsNote = true, WorkingDirectory = @"C:\work\shop" }.ProjectKey);
        Assert.Null(new TerminalNodeViewModel { Title = "t", CommandLine = "cmd" }.ProjectKey);
        Assert.Null(ProjectColors.For(null));
        Assert.Equal(ProjectColors.Palette.Count, ProjectColors.Palette.Distinct().Count());
    }
}
