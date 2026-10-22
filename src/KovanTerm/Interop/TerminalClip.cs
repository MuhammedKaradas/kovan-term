namespace KovanTerm.Interop;

/// <summary>
/// Terminal HWNDs ignore WPF clipping (airspace), so a terminal panned past the canvas edge would draw
/// over the toolbar/title bar (#41). Each terminal gets a window region equal to its intersection with
/// the canvas.
/// </summary>
internal static class TerminalClip
{
    /// <summary>
    /// Clips <paramref name="hwnd"/> to <paramref name="clip"/> (screen coordinates). Removes the region when
    /// the window is fully inside. Returns the region that was applied, in window coordinates (<c>null</c> = none),
    /// so callers can skip redundant updates.
    /// </summary>
    public static Native.RECT? Apply(IntPtr hwnd, Native.RECT clip, Native.RECT? current)
    {
        if (!Native.GetWindowRect(hwnd, out var r)) return current;

        var inside = r.Left >= clip.Left && r.Top >= clip.Top && r.Right <= clip.Right && r.Bottom <= clip.Bottom;
        if (inside)
        {
            if (current != null) Native.SetWindowRgn(hwnd, IntPtr.Zero, true);
            return null;
        }

        // Intersection, relative to the window's top-left corner; may be empty (fully clipped)
        var region = new Native.RECT
        {
            Left = Math.Clamp(clip.Left - r.Left, 0, r.Right - r.Left),
            Top = Math.Clamp(clip.Top - r.Top, 0, r.Bottom - r.Top),
            Right = Math.Clamp(clip.Right - r.Left, 0, r.Right - r.Left),
            Bottom = Math.Clamp(clip.Bottom - r.Top, 0, r.Bottom - r.Top),
        };
        if (current is { } c && c.Left == region.Left && c.Top == region.Top && c.Right == region.Right && c.Bottom == region.Bottom)
            return current;

        // The system owns the region after SetWindowRgn succeeds; delete it only on failure
        var rgn = Native.CreateRectRgn(region.Left, region.Top, region.Right, region.Bottom);
        if (Native.SetWindowRgn(hwnd, rgn, true) == 0)
        {
            Native.DeleteObject(rgn);
            return current;
        }
        return region;
    }
}
