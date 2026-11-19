using KovanTerm.Views;

namespace KovanTerm.Tests;

/// <summary>Zoom levels where live terminal text matches its scaled snapshot (#34).</summary>
public sealed class TerminalFontStepsTests
{
    // Cascadia Code's metrics; measured cell sizes at 96 DPI: 11 pt 9 × 17 px, 12 pt 9 × 19, 13 pt 10 × 20
    private static readonly TerminalFontSteps Cascadia96 = new(dpi: 96, advanceEm: 0.5859375, heightEm: 1.162109375);

    private static TerminalFontSteps.Step StepOf(int pt) => Cascadia96.Steps.Single(s => s.FontSize == pt);

    [Theory]
    [InlineData(13, 1.0)]
    [InlineData(8, 0.6)]   // 6 × 12 px: both ratios 0.6
    [InlineData(9, 0.7)]   // 7 × 14
    [InlineData(14, 1.1)]  // 11 × 22
    public void WhereWidthAndHeightAgree_TheStepIsExact(int pt, double zoom)
    {
        Assert.Equal(zoom, StepOf(pt).Zoom, 6);
        Assert.Equal(0, StepOf(pt).Error, 6);
    }

    [Fact]
    public void WhereTheyDont_TheStepIsHalfwayBetween()
    {
        // 11 pt: 9/10 wide, 17/20 tall
        Assert.Equal(0.875, StepOf(11).Zoom, 6);
        Assert.Equal(0.025 / 0.875, StepOf(11).Error, 6);
        // 12 pt: 9/10 wide, 19/20 tall
        Assert.Equal(0.925, StepOf(12).Zoom, 6);
    }

    [Fact]
    public void Steps_AreOrderedAndStayCloseInTheUsableRange()
    {
        var zooms = Cascadia96.Steps.Select(s => s.Zoom).ToList();
        Assert.Equal(zooms.Order(), zooms);
        // Terminals show live text from 40 % up to 150 %
        Assert.All(Cascadia96.Steps.Where(s => s.Zoom is >= 0.4 and <= 1.5), s => Assert.True(s.Error < 0.06, $"{s.FontSize} pt: {s.Error:P1}"));
    }

    [Theory]
    [InlineData(0.87, 11, 0.875)]
    [InlineData(0.91, 12, 0.925)]
    [InlineData(1.0, 13, 1.0)]
    [InlineData(1.04, 13, 1.0)]
    [InlineData(0.62, 8, 0.6)]
    public void Nearest_GivesTheFontAndTheZoomToSnapTo(double zoom, int pt, double snapped)
    {
        Assert.Equal(pt, Cascadia96.FontSizeFor(zoom));
        Assert.Equal(snapped, Cascadia96.SnappedZoom(zoom, 0.3, 1.5), 6);
    }

    [Fact]
    public void SnappedZoom_StaysWithinTheEditorsLimits()
    {
        var max = Cascadia96.SnappedZoom(1.5, 0.3, 1.5);
        Assert.True(max <= 1.5);
        Assert.True(Cascadia96.SnappedZoom(0.3, 0.3, 1.5) >= 0.3);
    }

    [Fact]
    public void HigherDpi_HasItsOwnSteps()
    {
        // 150 %: 13 pt is 26 px, cell round(15.23) = 15
        var hi = new TerminalFontSteps(dpi: 144, advanceEm: 0.5859375, heightEm: 1.162109375);
        Assert.Equal(1.0, hi.Steps.Single(s => s.FontSize == 13).Zoom, 6);
        Assert.NotEqual(StepOf(11).Zoom, hi.Steps.Single(s => s.FontSize == 11).Zoom);
    }

    [Fact]
    public void WithoutFontMetrics_ItsPlainFontSizeTimesZoom()
    {
        var plain = new TerminalFontSteps(dpi: 72, advanceEm: 1, heightEm: 1);
        Assert.Equal(11.0 / 13, plain.Steps.Single(s => s.FontSize == 11).Zoom, 6);
        Assert.Equal(11, plain.FontSizeFor(0.86));
    }
}
