using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KovanTerm.Interop;

/// <summary>
/// The terminal control renders with DirectX in its own HWND, so it doesn't scale with
/// WPF transforms. While panning/zooming, a snapshot taken by this class is shown
/// instead of the live terminal.
/// </summary>
/// <remarks>
/// The top-level window is captured once with <c>PrintWindow(PW_RENDERFULLCONTENT)</c> and each
/// terminal is cropped out of it. Capturing each terminal HWND directly was unreliable once a terminal
/// was clipped (by the window edge or by its window region): depending on timing, PrintWindow returned
/// the visible part either in place or aligned to the top-left corner. One capture of the whole window
/// also costs one DWM frame (~15 ms) instead of one per terminal.
/// </remarks>
internal static class TerminalSnapshot
{
    // PW_RENDERFULLCONTENT: also captures DirectX-rendered content
    private const uint PwRenderFullContent = 2;

    /// <param name="Hwnd">The terminal's native window (used to find the top-level window).</param>
    /// <param name="Window">The whole terminal control's rectangle (screen coordinates), including its WPF
    /// scrollbar, so the snapshot matches the node body exactly and isn't stretched.</param>
    /// <param name="Visible">The part to keep in the snapshot; the rest is painted with the background.</param>
    public sealed record Target(IntPtr Hwnd, Native.RECT Window, Native.RECT Visible);

    /// <summary>
    /// HWND and visible-area info; must be obtained on the UI thread. <paramref name="clip"/> is the canvas
    /// area in screen coordinates (the part of the terminal outside it isn't visible, see <see cref="TerminalClip"/>).
    /// </summary>
    public static Target? GetTarget(FrameworkElement terminal, Native.RECT? clip = null)
    {
        if (FindHwnd(terminal) is not { } hwnd || PresentationSource.FromVisual(terminal) == null) return null;
        var topLeft = terminal.PointToScreen(new Point(0, 0));
        var bottomRight = terminal.PointToScreen(new Point(terminal.ActualWidth, terminal.ActualHeight));
        var r = new Native.RECT { Left = (int)Math.Round(topLeft.X), Top = (int)Math.Round(topLeft.Y), Right = (int)Math.Round(bottomRight.X), Bottom = (int)Math.Round(bottomRight.Y) };
        if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return null;
        var visible = Intersect(r, ClientRect(hwnd) ?? r);
        if (clip is { } c) visible = Intersect(visible, c);
        return new Target(hwnd, r, visible);
    }

    /// <summary>The terminal's native window, or <c>null</c> if it hasn't been created yet.</summary>
    public static IntPtr? FindHwnd(DependencyObject terminal) =>
        FindDescendant<HwndHost>(terminal) is { Handle: var h } && h != IntPtr.Zero ? h : null;

    /// <summary>
    /// Captures the targets with a single capture of their top-level window. Can be called on any thread;
    /// the returned images are frozen (<see cref="Freezable.Freeze"/>). <c>null</c> for any that can't be captured.
    /// </summary>
    public static BitmapSource?[] CaptureAll(IReadOnlyList<Target> targets, Color background)
    {
        var result = new BitmapSource?[targets.Count];
        if (targets.Count == 0) return result;

        var root = Native.GetAncestor(targets[0].Hwnd, Native.GA_ROOT);
        if (root == IntPtr.Zero || !Native.GetWindowRect(root, out var rootRect)) return result;
        int rw = rootRect.Right - rootRect.Left, rh = rootRect.Bottom - rootRect.Top;
        if (rw <= 0 || rh <= 0) return result;

        var screenDc = Native.GetDC(IntPtr.Zero);
        var rootDc = Native.CreateCompatibleDC(screenDc);
        var rootBmp = Native.CreateCompatibleBitmap(screenDc, rw, rh);
        var oldRoot = Native.SelectObject(rootDc, rootBmp);
        try
        {
            if (!Native.PrintWindow(root, rootDc, PwRenderFullContent)) return result;
            for (var i = 0; i < targets.Count; i++)
                result[i] = Crop(screenDc, rootDc, rootRect, targets[i], background);
            return result;
        }
        finally
        {
            Native.SelectObject(rootDc, oldRoot);
            Native.DeleteObject(rootBmp);
            Native.DeleteDC(rootDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Copies the terminal's visible part out of the window capture; the rest is the terminal background.</summary>
    private static BitmapSource? Crop(IntPtr screenDc, IntPtr rootDc, Native.RECT rootRect, Target t, Color background)
    {
        int w = t.Window.Right - t.Window.Left, h = t.Window.Bottom - t.Window.Top;
        var memDc = Native.CreateCompatibleDC(screenDc);
        var bmp = Native.CreateCompatibleBitmap(screenDc, w, h);
        var old = Native.SelectObject(memDc, bmp);
        try
        {
            var brush = Native.CreateSolidBrush((uint)(background.R | background.G << 8 | background.B << 16));
            var all = new Native.RECT { Left = 0, Top = 0, Right = w, Bottom = h };
            Native.FillRect(memDc, ref all, brush);
            Native.DeleteObject(brush);

            int vw = t.Visible.Right - t.Visible.Left, vh = t.Visible.Bottom - t.Visible.Top;
            if (vw > 0 && vh > 0)
                Native.BitBlt(memDc, t.Visible.Left - t.Window.Left, t.Visible.Top - t.Window.Top, vw, vh,
                    rootDc, t.Visible.Left - rootRect.Left, t.Visible.Top - rootRect.Top, Native.SRCCOPY);

            Native.SelectObject(memDc, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            Native.DeleteObject(bmp);
            Native.DeleteDC(memDc);
        }
    }

    /// <summary>The top-level window's client area (screen coordinates).</summary>
    private static Native.RECT? ClientRect(IntPtr hwnd)
    {
        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        if (root == IntPtr.Zero || !Native.GetClientRect(root, out var client)) return null;
        var origin = new Native.POINT();
        Native.ClientToScreen(root, ref origin);
        return new Native.RECT { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
    }

    private static Native.RECT Intersect(Native.RECT a, Native.RECT b) => new()
    {
        Left = Math.Max(a.Left, b.Left),
        Top = Math.Max(a.Top, b.Top),
        Right = Math.Min(a.Right, b.Right),
        Bottom = Math.Min(a.Bottom, b.Bottom),
    };

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
