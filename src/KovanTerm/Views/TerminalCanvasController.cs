using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyWindowsTerminalControl;
using KovanTerm.Interop;
using Nodify;

namespace KovanTerm.Views;

/// <summary>
/// Tuval üzerindeki terminal kontrollerinin HwndHost kısıtlarını yönetir:
/// <list type="bullet">
/// <item>Kaydırma/zoom/sürükleme sırasında canlı terminaller yerine anlık görüntülerini gösterir ("fotoğraf modu").</item>
/// <item>HWND, WPF zoom'uyla ölçeklenmediği için yazı boyutunu zoom'a göre ayarlar.</item>
/// </list>
/// Terminal kontrolünün, kardeşi bir <see cref="Image"/> olan bir <see cref="Grid"/> içinde durması beklenir.
/// </summary>
public sealed class TerminalCanvasController
{
    private const int BaseFontSize = 13;
    private const int MinFontSize = 4;

    private readonly NodifyEditor _editor;
    private readonly List<EasyTerminalControl> _terminals = new();

    // Hareket bittikten bu kadar sonra canlı terminallere geri dönülür
    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _frozen;
    private int _appliedFontSize = BaseFontSize;

    public TerminalCanvasController(NodifyEditor editor)
    {
        _editor = editor;
        _settleTimer.Tick += (_, _) => { _settleTimer.Stop(); Unfreeze(); };
        _editor.ViewportUpdated += (_, _) => OnCanvasMoving();
        _editor.AddHandler(ItemContainer.LocationChangedEvent, new RoutedEventHandler((_, _) => OnCanvasMoving()));
    }

    public void Register(EasyTerminalControl term)
    {
        if (_terminals.Contains(term)) return;
        _terminals.Add(term);
        term.Unloaded += (_, _) => _terminals.Remove(term);
        // Terminal hazır olunca mevcut zoom'a uygun yazı boyutunu uygula
        term.Dispatcher.BeginInvoke(() => SetFont(term, _appliedFontSize), DispatcherPriority.ApplicationIdle);
    }

    private void OnCanvasMoving()
    {
        if (!_editor.IsLoaded) return;
        Freeze();
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void Freeze()
    {
        if (_frozen) return;
        _frozen = true;
        foreach (var term in _terminals)
        {
            if (GetSnapshotImage(term) is not { } image) continue;
            var shot = TerminalSnapshot.Capture(term);
            if (shot == null) continue;
            image.Source = shot;
            image.Visibility = Visibility.Visible;
            term.Visibility = Visibility.Hidden;
        }
    }

    private void Unfreeze()
    {
        if (!_frozen) return;
        _frozen = false;
        ApplyZoomToTerminals();
        foreach (var term in _terminals)
            term.Visibility = Visibility.Visible;
        // Terminal yeniden çizilene kadar görüntü altta kalsın, sonra kaldır
        _editor.Dispatcher.BeginInvoke(() =>
        {
            if (_frozen) return;
            foreach (var term in _terminals)
                if (GetSnapshotImage(term) is { } image) { image.Visibility = Visibility.Collapsed; image.Source = null; }
        }, DispatcherPriority.Background);
    }

    private static Image? GetSnapshotImage(EasyTerminalControl term) =>
        (term.Parent as Grid)?.Children.OfType<Image>().FirstOrDefault();

    private void ApplyZoomToTerminals()
    {
        var size = Math.Max(MinFontSize, (int)Math.Round(BaseFontSize * _editor.ViewportZoom));
        if (size == _appliedFontSize) return;
        _appliedFontSize = size;
        foreach (var term in _terminals)
            SetFont(term, size);
    }

    private static void SetFont(EasyTerminalControl term, int size) =>
        term.Terminal?.SetTheme(TerminalThemes.Dark, "Cascadia Code", (short)size);
}
