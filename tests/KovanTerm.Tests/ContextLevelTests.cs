using KovanTerm.Models;
using KovanTerm.ViewModels;
using KovanTerm.Views;

namespace KovanTerm.Tests;

/// <summary>The context meter's color, and which brush a status dot gets.</summary>
public sealed class ContextLevelTests
{
    [Theory]
    [InlineData(100_000, "claude", null, 200_000)]
    [InlineData(150_000, "claude --model opus[1m]", null, 1_000_000)]
    [InlineData(150_000, "claude", "deepseek-flash[1m]", 1_000_000)]
    // Past 200k only a 1M model can be
    [InlineData(250_000, "claude", null, 1_000_000)]
    public void Context_window(long tokens, string commandLine, string? model, long window) =>
        Assert.Equal(window, TerminalNodeViewModel.ContextWindow(tokens, commandLine, model));

    [Theory]
    [InlineData(149_999, ContextLevel.Normal)]
    [InlineData(150_000, ContextLevel.High)]
    [InlineData(179_999, ContextLevel.High)]
    [InlineData(180_000, ContextLevel.Full)]
    public void Amber_from_75_and_red_from_90_percent(long tokens, ContextLevel level) =>
        Assert.Equal(level, TerminalNodeViewModel.LevelOf(tokens, 200_000));

    [Fact]
    public void Dots_of_a_status_share_one_brush()
    {
        Assert.Same(StatusBrushes.Working, StatusBrushes.For(NodeStatus.Working));
        Assert.Same(StatusBrushes.Waiting, StatusBrushes.For(NodeStatus.Waiting));
        Assert.Same(StatusBrushes.Idle, StatusBrushes.For(NodeStatus.Idle));
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.2)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.5, 0.6)]
    public void A_breath_goes_from_full_to_low_and_back(double seconds, double opacity) =>
        Assert.Equal(opacity, StatusBrushes.OpacityAt(seconds, period: 2.0, low: 0.2), precision: 6);

    [Fact]
    public void The_colour_blind_palette_recolours_the_shared_brushes()
    {
        var resources = new System.Windows.ResourceDictionary();
        try
        {
            StatusBrushes.ApplyPalette(colorBlind: true, resources);
            Assert.Equal(StatusBrushes.ColorBlind.Working, StatusBrushes.Working.Color);
            Assert.Equal(StatusBrushes.ColorBlind.Error, StatusBrushes.Error.Color);
            Assert.Same(StatusBrushes.Done, StatusBrushes.For(NodeStatus.Done));
            Assert.Equal(StatusBrushes.ColorBlind.Added, ((System.Windows.Media.SolidColorBrush)resources[StatusBrushes.DiffAddedKey]).Color);
        }
        finally
        {
            StatusBrushes.ApplyPalette(colorBlind: false, resources);
        }
        Assert.Equal(StatusBrushes.Default.Working, StatusBrushes.Working.Color);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(12, "9+")]
    public void Taskbar_badge_text(int waiting, string? text) => Assert.Equal(text, TaskbarBadge.Text(waiting));
}
