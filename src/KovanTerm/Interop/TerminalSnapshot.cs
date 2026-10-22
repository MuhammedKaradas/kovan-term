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
internal static class TerminalSnapshot
{
    // PW_RENDERFULLCONTENT: also captures DirectX-rendered content
    private const uint PwRenderFullContent = 2;

    /// <summary>
    /// Captures the terminals. Since PrintWindow waits roughly one DWM frame per call
    /// (~12-20 ms per terminal), captures run in parallel; the total time drops to ~one frame
    /// regardless of the terminal count. The result is <c>null</c> for any that can't be captured.
    /// </summary>
    /// <summary>HWND and visible-area info; must be obtained on the UI thread.</summary>
    public static Target? GetTarget(DependencyObject terminal)
    {
        var host = FindDescendant<HwndHost>(terminal);
        if (host == null || host.Handle == IntPtr.Zero || !Native.GetWindowRect(host.Handle, out var r)) return null;
        if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return null;
        return new Target(host.Handle, r, VisibleRect(host.Handle, r));
    }

    /// <summary>
    /// Captures the target. Can be called on any thread; the returned image is frozen
    /// (<see cref="Freezable.Freeze"/>), so it can be used from another thread.
    /// PrintWindow takes ~12-20 ms per terminal (it waits for a DWM frame).
    /// </summary>
    public static BitmapSource? Capture(Target target, Color background)
    {
        var hbmp = CaptureHBitmap(target, background);
        if (hbmp == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            Native.DeleteObject(hbmp);
        }
    }

    public sealed record Target(IntPtr Hwnd, Native.RECT Window, Native.RECT Visible);

    /// <summary>The part of the HWND that lies within the top-level window's client area (screen coordinates).</summary>
    private static Native.RECT VisibleRect(IntPtr hwnd, Native.RECT r)
    {
        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        if (root == IntPtr.Zero || !Native.GetClientRect(root, out var client)) return r;
        var origin = new Native.POINT();
        Native.ClientToScreen(root, ref origin);
        return new Native.RECT
        {
            Left = Math.Max(r.Left, origin.X),
            Top = Math.Max(r.Top, origin.Y),
            Right = Math.Min(r.Right, origin.X + client.Right),
            Bottom = Math.Min(r.Bottom, origin.Y + client.Bottom),
        };
    }

    private static IntPtr CaptureHBitmap(Target t, Color background)
    {
        int w = t.Window.Right - t.Window.Left, h = t.Window.Bottom - t.Window.Top;
        // Visible area, in bitmap coordinates
        int vl = t.Visible.Left - t.Window.Left, vt = t.Visible.Top - t.Window.Top;
        int vw = t.Visible.Right - t.Visible.Left, vh = t.Visible.Bottom - t.Visible.Top;

        var screenDc = Native.GetDC(IntPtr.Zero);
        var memDc = Native.CreateCompatibleDC(screenDc);
        var bmp = Native.CreateCompatibleBitmap(screenDc, w, h);
        var old = Native.SelectObject(memDc, bmp);
        try
        {
            Native.PrintWindow(t.Hwnd, memDc, PwRenderFullContent);
            // If the window is clipped on the left/top, PrintWindow draws only the visible part
            // aligned to the top-left corner; shift it to its correct position
            if ((vl > 0 || vt > 0) && vw > 0 && vh > 0)
                Native.BitBlt(memDc, vl, vt, vw, vh, memDc, 0, 0, Native.SRCCOPY);
            FillOutsideVisible(memDc, vl, vt, vl + vw, vt + vh, w, h, background);
            return bmp;
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// The part outside the window comes back blank/gray from PrintWindow; it's painted with the terminal
    /// background so no gray strip shows while moving. (vl, vt, vr, vb): visible area, in bitmap coordinates.
    /// </summary>
    private static void FillOutsideVisible(IntPtr dc, int vl, int vt, int vr, int vb, int w, int h, Color background)
    {
        if (vl <= 0 && vt <= 0 && vr >= w && vb >= h) return;

        var brush = Native.CreateSolidBrush((uint)(background.R | background.G << 8 | background.B << 16));
        try
        {
            if (vr <= vl || vb <= vt)
            {
                Fill(0, 0, w, h); // entirely hidden
                return;
            }
            if (vt > 0) Fill(0, 0, w, vt);
            if (vb < h) Fill(0, vb, w, h);
            if (vl > 0) Fill(0, vt, vl, vb);
            if (vr < w) Fill(vr, vt, w, vb);
        }
        finally
        {
            Native.DeleteObject(brush);
        }

        void Fill(int l, int top, int r, int b)
        {
            var rect = new Native.RECT { Left = l, Top = top, Right = r, Bottom = b };
            Native.FillRect(dc, ref rect, brush);
        }
    }

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
