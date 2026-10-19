using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KovanTerm.Interop;

/// <summary>
/// Terminal kontrolü kendi HWND'sinde DirectX ile çizildiği için WPF dönüşümleriyle
/// ölçeklenmez. Kaydırma/zoom sırasında canlı terminal yerine bu sınıfın aldığı
/// anlık görüntü gösterilir.
/// </summary>
internal static class TerminalSnapshot
{
    // PW_RENDERFULLCONTENT: DirectX ile çizilen içeriği de yakalar
    private const uint PwRenderFullContent = 2;

    /// <summary>
    /// Terminallerin görüntülerini alır. PrintWindow her çağrıda yaklaşık bir DWM karesi beklediği
    /// için (terminal başına ~12-20 ms) görüntüler paralel alınır; toplam süre terminal sayısından
    /// bağımsız olarak ~bir kareye iner. Alınamayanlar için sonuç <c>null</c>.
    /// </summary>
    /// <summary>HWND ve görünür alan bilgisi; arayüz iş parçacığında alınmalı.</summary>
    public static Target? GetTarget(DependencyObject terminal)
    {
        var host = FindDescendant<HwndHost>(terminal);
        if (host == null || host.Handle == IntPtr.Zero || !Native.GetWindowRect(host.Handle, out var r)) return null;
        if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return null;
        return new Target(host.Handle, r, VisibleRect(host.Handle, r));
    }

    /// <summary>
    /// Hedefin görüntüsünü alır. Herhangi bir iş parçacığında çağrılabilir; dönen görüntü dondurulmuş
    /// (<see cref="Freezable.Freeze"/>) olduğu için başka iş parçacığında kullanılabilir.
    /// PrintWindow terminal başına ~12-20 ms sürer (DWM karesini bekler).
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

    /// <summary>HWND'nin üst pencerenin istemci alanında kalan kısmı (ekran koordinatı).</summary>
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
        // Görünür alan, bitmap koordinatlarında
        int vl = t.Visible.Left - t.Window.Left, vt = t.Visible.Top - t.Window.Top;
        int vw = t.Visible.Right - t.Visible.Left, vh = t.Visible.Bottom - t.Visible.Top;

        var screenDc = Native.GetDC(IntPtr.Zero);
        var memDc = Native.CreateCompatibleDC(screenDc);
        var bmp = Native.CreateCompatibleBitmap(screenDc, w, h);
        var old = Native.SelectObject(memDc, bmp);
        try
        {
            Native.PrintWindow(t.Hwnd, memDc, PwRenderFullContent);
            // Pencere soldan/üstten kırpılmışsa PrintWindow yalnızca görünür kısmı sol üst köşeye
            // yaslanmış olarak çizer; doğru konumuna kaydır
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
    /// Pencere dışına taşan kısım PrintWindow'da boş/gri gelir; terminal arka planıyla boyanır ki
    /// hareket sırasında gri şerit görünmesin. (vl, vt, vr, vb): görünür alan, bitmap koordinatlarında.
    /// </summary>
    private static void FillOutsideVisible(IntPtr dc, int vl, int vt, int vr, int vb, int w, int h, Color background)
    {
        if (vl <= 0 && vt <= 0 && vr >= w && vb >= h) return;

        var brush = Native.CreateSolidBrush((uint)(background.R | background.G << 8 | background.B << 16));
        try
        {
            if (vr <= vl || vb <= vt)
            {
                Fill(0, 0, w, h); // tamamen görünmüyor
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
