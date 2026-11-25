using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using KovanTerm.Services;

namespace KovanTerm.Views;

/// <summary>
/// The palette of project colors (#145): which project has which is <see cref="ProjectColorMap"/>'s job.
/// </summary>
public static class ProjectColors
{
    // Mid-tone colors that read on both the dark and the light theme, and aren't the status colors' exact hues
    public static readonly IReadOnlyList<Color> Palette =
    [
        Color.FromRgb(0x61, 0xAF, 0xEF), // blue
        Color.FromRgb(0xE0, 0x6C, 0x75), // red
        Color.FromRgb(0x98, 0xC3, 0x79), // green
        Color.FromRgb(0xC6, 0x78, 0xDD), // purple
        Color.FromRgb(0xD1, 0x9A, 0x66), // orange
        Color.FromRgb(0x56, 0xB6, 0xC2), // teal
        Color.FromRgb(0xFF, 0x79, 0xC6), // pink
        Color.FromRgb(0xB8, 0xA9, 0x6A), // olive
    ];

    private static readonly SolidColorBrush[] Brushes = Palette.Select(c =>
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }).ToArray();

    /// <summary>The project's brush; <c>null</c> when there's no project or it has no color yet.</summary>
    public static Brush? For(string? key) => ProjectColorMap.Current.Find(key) is { } i ? Brushes[i] : null;
}

/// <summary>Project key → its brush; transparent when there's none.</summary>
public sealed class ProjectKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ProjectColors.For(value as string) ?? System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
