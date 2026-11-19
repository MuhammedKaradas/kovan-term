using System.Windows;
using System.Windows.Media;

namespace KovanTerm.Views;

/// <summary>
/// For each font size the terminal can render at, the canvas zoom at which the live terminal looks most like its
/// snapshot scaled by the canvas (#34).
/// <para>
/// The terminal renders at a whole font size, and Windows Terminal's renderer rounds the cell width and the line height
/// to whole device pixels. Measured with Cascadia Code at 96 DPI: 13 pt is 10 × 20 px, 11 pt 9 × 17, 12 pt 9 × 19
/// (round(0.586 × px) × round(1.162 × px), for every size from 6 to 16 pt). So text at 11 pt is 0.90 × as wide and
/// 0.85 × as tall as at 13 pt, not 11/13 of both, and wherever the zoom lands the live text comes back a little wider,
/// narrower, taller or shorter than the snapshot. A step's zoom is halfway between its width and height ratios, which
/// leaves the smallest difference on both axes (none at all where they agree: 60 %, 70 %, 100 %, 110 % at 96 DPI).
/// </para>
/// </summary>
public sealed class TerminalFontSteps
{
    public const int BaseFontSize = 13;
    public const int MinFontSize = 4;
    public const int MaxFontSize = 40;

    /// <param name="Error">How far apart live text and snapshot still are at <see cref="Zoom"/>, on each axis (0.03 = 3 %).</param>
    public readonly record struct Step(int FontSize, double Zoom, double Error);

    /// <summary>One per font size, by zoom.</summary>
    public IReadOnlyList<Step> Steps { get; }

    /// <param name="advanceEm">The font's cell advance in ems (Cascadia Code: 0.586).</param>
    /// <param name="heightEm">The font's line height in ems (Cascadia Code: 1.162).</param>
    public TerminalFontSteps(double dpi, double advanceEm, double heightEm)
    {
        double Px(int pt) => pt * dpi / 72;
        double Width(int pt) => Math.Max(1, Math.Round(advanceEm * Px(pt), MidpointRounding.AwayFromZero));
        double Height(int pt) => Math.Max(1, Math.Round(heightEm * Px(pt), MidpointRounding.AwayFromZero));
        double baseWidth = Width(BaseFontSize), baseHeight = Height(BaseFontSize);
        Steps = Enumerable.Range(MinFontSize, MaxFontSize - MinFontSize + 1)
            .Select(pt =>
            {
                double w = Width(pt) / baseWidth, h = Height(pt) / baseHeight;
                var zoom = (w + h) / 2;
                return new Step(pt, zoom, Math.Abs(w - h) / 2 / zoom);
            })
            // Two sizes with the same metrics are one step
            .DistinctBy(s => Math.Round(s.Zoom, 6))
            .OrderBy(s => s.Zoom)
            .ToList();
    }

    /// <summary>The steps for <paramref name="fontFamily"/> at <paramref name="dpi"/>; plain font size × zoom if the font can't be read.</summary>
    public static TerminalFontSteps For(string fontFamily, double dpi)
    {
        var typeface = new Typeface(new FontFamily(fontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.TryGetValue('M', out var m))
            return new TerminalFontSteps(dpi, glyphs.AdvanceWidths[m], glyphs.Height);
        return new TerminalFontSteps(dpi: 72, advanceEm: 1, heightEm: 1);
    }

    /// <summary>The font size to render at <paramref name="zoom"/>: the nearest step's.</summary>
    public int FontSizeFor(double zoom) => Nearest(zoom, 0, double.MaxValue).FontSize;

    /// <summary>The nearest step's zoom within [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public double SnappedZoom(double zoom, double min, double max) => Nearest(zoom, min, max).Zoom;

    private Step Nearest(double zoom, double min, double max)
    {
        var inRange = Steps.Where(s => s.Zoom >= min - 1e-9 && s.Zoom <= max + 1e-9).ToList();
        return (inRange.Count > 0 ? inRange : Steps).MinBy(s => Math.Abs(s.Zoom - zoom));
    }
}
