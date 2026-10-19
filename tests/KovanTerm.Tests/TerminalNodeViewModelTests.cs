using System.Windows;
using KovanTerm.Models;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class TerminalNodeViewModelTests
{
    private static TerminalNodeViewModel Node() => new() { Title = "cmd", CommandLine = "cmd.exe", Location = new Point(10, 20) };

    [Fact]
    public void ResizeBy_BoyutuDegistirirVeSinirlariUygular()
    {
        var node = Node();

        node.ResizeBy(100, 50);
        Assert.Equal(TerminalNodeViewModel.DefaultWidth + 100, node.Width);
        Assert.Equal(TerminalNodeViewModel.DefaultHeight + 50, node.Height);

        node.ResizeBy(-10_000, -10_000);
        Assert.Equal(TerminalNodeViewModel.MinWidth, node.Width);
        Assert.Equal(TerminalNodeViewModel.MinHeight, node.Height);

        node.ResizeBy(10_000, 10_000);
        Assert.Equal(TerminalNodeViewModel.MaxSize, node.Width);
        Assert.Equal(TerminalNodeViewModel.MaxSize, node.Height);
    }

    [Fact]
    public void Bounds_CerceveyiIcerirVeBoyutDegisinceBildirilir()
    {
        var node = Node();
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.ResizeBy(40, 0);

        Assert.Equal(new Rect(10, 20, TerminalNodeViewModel.DefaultWidth + 40,
            TerminalNodeViewModel.DefaultHeight + TerminalNodeViewModel.ChromeHeight), node.Bounds);
        Assert.Contains(nameof(TerminalNodeViewModel.Bounds), changed);
    }

    [Fact]
    public void StateDonusumu_BoyutuKorurVeSinirDisiniDuzeltir()
    {
        var node = Node();
        node.ResizeBy(200, 100);
        var roundTrip = TerminalNodeViewModel.FromState(node.ToState());
        Assert.Equal(node.Width, roundTrip.Width);
        Assert.Equal(node.Height, roundTrip.Height);

        var tiny = TerminalNodeViewModel.FromState(new NodeState { Title = "x", CommandLine = "x", Width = 5, Height = 5 });
        Assert.Equal(TerminalNodeViewModel.MinWidth, tiny.Width);
        Assert.Equal(TerminalNodeViewModel.MinHeight, tiny.Height);
    }
}
