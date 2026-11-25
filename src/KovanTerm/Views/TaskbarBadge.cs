using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace KovanTerm.Views;

/// <summary>
/// The number of agents waiting for the user, as a badge on Kovan's taskbar button (the overlay icon), so it shows
/// whatever is in front. Nothing while none waits.
/// </summary>
public static class TaskbarBadge
{
    private const int Size = 32;
    private static readonly Dictionary<(string, Color), ImageSource> Cache = new();

    /// <summary>"1"…"9", then "9+"; <c>null</c> for none.</summary>
    public static string? Text(int waiting) => waiting <= 0 ? null : waiting > 9 ? "9+" : waiting.ToString(CultureInfo.InvariantCulture);

    public static void Show(Window window, int waiting, Color color)
    {
        window.TaskbarItemInfo ??= new TaskbarItemInfo();
        var info = window.TaskbarItemInfo;
        if (Text(waiting) is not { } text)
        {
            info.Overlay = null;
            info.Description = "Kovan Term";
            return;
        }
        if (!Cache.TryGetValue((text, color), out var image)) Cache[(text, color)] = image = Draw(text, color);
        info.Overlay = image;
        info.Description = waiting == 1 ? "Kovan Term: an agent is waiting for you" : $"Kovan Term: {waiting} agents are waiting for you";
    }

    /// <summary>A round badge in the waiting colour with the count in dark text.</summary>
    private static ImageSource Draw(string text, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), 2),
                new Point(Size / 2.0, Size / 2.0), Size / 2.0 - 1, Size / 2.0 - 1);
            var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                text.Length > 1 ? 15 : 20, new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), 1.0);
            dc.DrawText(label, new Point((Size - label.Width) / 2, (Size - label.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
