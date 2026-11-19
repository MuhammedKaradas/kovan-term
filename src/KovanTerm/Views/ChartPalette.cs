using System.Windows;
using System.Windows.Media;

namespace KovanTerm.Views;

/// <summary>
/// Chart colors as app resources (<c>ChartSeries1</c>–<c>7</c>, <c>ChartOther</c>, <c>ChartLevel0</c>–<c>4</c>), stepped
/// separately for the light and dark themes. The categorical order is a validated colorblind-safe order (adjacent
/// slots stay apart under each kind of color blindness); the calendar uses one hue from light to dark.
/// </summary>
public static class ChartPalette
{
    private static readonly string[] SeriesLight = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6"];
    private static readonly string[] SeriesDark = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#0ca30c", "#9085e9"];
    // Nothing, then busier days: towards the surface's opposite, so the busiest stand out on either theme
    private static readonly string[] LevelsLight = ["#dddcd7", "#b7d3f6", "#6da7ec", "#2a78d6", "#184f95"];
    private static readonly string[] LevelsDark = ["#3d3d3a", "#104281", "#1c5cab", "#3987e5", "#86b6ef"];

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        var series = dark ? SeriesDark : SeriesLight;
        for (var i = 0; i < series.Length; i++) resources[$"ChartSeries{i + 1}"] = Brush(series[i]);
        resources["ChartOther"] = Brush(dark ? "#6d6c67" : "#a3a29d");
        var levels = dark ? LevelsDark : LevelsLight;
        for (var i = 0; i < levels.Length; i++) resources[$"ChartLevel{i}"] = Brush(levels[i]);
        resources["ChartBar"] = Brush(series[0]);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
