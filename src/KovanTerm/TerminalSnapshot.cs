using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KovanTerm;

/// <summary>
/// Terminal kontrolü kendi HWND'sinde DirectX ile çizildiği için WPF dönüşümleriyle
/// ölçeklenmez. Kaydırma/zoom sırasında canlı terminal yerine bu sınıfın aldığı
/// anlık görüntü gösterilir.
/// </summary>
internal static class TerminalSnapshot
{
    // PW_RENDERFULLCONTENT: DirectX ile çizilen içeriği de yakalar
    private const uint PwRenderFullContent = 2;

    public static BitmapSource? Capture(DependencyObject terminal)
    {
        var host = FindDescendant<HwndHost>(terminal);
        if (host == null || host.Handle == IntPtr.Zero || !Native.GetWindowRect(host.Handle, out var r))
            return null;

        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return null;

        var screenDc = Native.GetDC(IntPtr.Zero);
        var memDc = Native.CreateCompatibleDC(screenDc);
        var bmp = Native.CreateCompatibleBitmap(screenDc, w, h);
        var old = Native.SelectObject(memDc, bmp);
        try
        {
            Native.PrintWindow(host.Handle, memDc, PwRenderFullContent);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
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
