using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyWindowsTerminalControl;
using KovanTerm.Interop;
using Microsoft.Terminal.Wpf;
using Nodify;

namespace KovanTerm.Views;

/// <summary>
/// Tuval üzerindeki terminal kontrollerinin HwndHost kısıtlarını yönetir:
/// <list type="bullet">
/// <item>Kaydırma/zoom/sürükleme sırasında canlı terminaller yerine anlık görüntülerini gösterir ("fotoğraf modu").</item>
/// <item>HWND, WPF zoom'uyla ölçeklenmediği için yazı boyutunu zoom'a göre ayarlar.</item>
/// <item>Düğüm kapanınca terminalin işlem ağacını sonlandırır.</item>
/// </list>
/// Terminal kontrolünün, kardeşi bir <see cref="Image"/> olan bir <see cref="Grid"/> içinde durması beklenir.
/// </summary>
public sealed class TerminalCanvasController
{
    private const int BaseFontSize = 13;
    private const int MinFontSize = 4;

    private readonly NodifyEditor _editor;
    // Anahtar: terminalin DataContext'i (düğüm ViewModel'i). Görsel ağaçtan bağımsız
    // tutulur ki düğüm listeden çıkarıldığında terminal hâlâ bulunabilsin.
    private readonly Dictionary<object, EasyTerminalControl> _byNode = new();
    private IEnumerable<EasyTerminalControl> _terminals => _byNode.Values;

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
        if (term.DataContext is not { } node || _byNode.ContainsKey(node)) return;
        _byNode[node] = term;
        // Terminal hazır olunca mevcut zoom'a uygun yazı boyutunu uygula (çalışma alanı farklı bir zoom'la açılmış olabilir)
        term.Dispatcher.BeginInvoke(() =>
        {
            _appliedFontSize = FontSizeForZoom(_editor.ViewportZoom);
            SetFont(term, _appliedFontSize);
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Düğümün terminalini ve altındaki tüm işlemleri (ör. cmd → claude → MCP sunucuları) sonlandırır.</summary>
    public void Terminate(object node)
    {
        if (!_byNode.Remove(node, out var term)) return;
        KillProcessTree(term);
    }

    public void TerminateAll()
    {
        foreach (var term in _byNode.Values)
            KillProcessTree(term);
        _byNode.Clear();
    }

    private static void KillProcessTree(EasyTerminalControl term)
    {
        var pty = term.ConPTYTerm;
        if (pty == null) return;
        try
        {
            if (pty.Process is { HasExited: false } process)
                process.Kill(EntireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // İşlem bu arada kendiliğinden kapanmış olabilir
            Debug.WriteLine($"Terminal işlemi sonlandırılamadı: {ex.Message}");
        }

        // Kütüphane işlem bitince pseudo-console'u kapatmıyor, conhost.exe açık kalıyor.
        // ClosePseudoConsole bloklayabildiği için arayüz iş parçacığında çağrılmaz.
        Task.Run(() =>
        {
            try { ((ITerminalConnection)pty).Close(); }
            catch (Exception ex) { Debug.WriteLine($"Pseudo-console kapatılamadı: {ex.Message}"); }
        });
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
        var size = FontSizeForZoom(_editor.ViewportZoom);
        if (size == _appliedFontSize) return;
        _appliedFontSize = size;
        foreach (var term in _terminals)
            SetFont(term, size);
    }

    private static int FontSizeForZoom(double zoom) => Math.Max(MinFontSize, (int)Math.Round(BaseFontSize * zoom));

    private static void SetFont(EasyTerminalControl term, int size) =>
        term.Terminal?.SetTheme(TerminalThemes.Dark, "Cascadia Code", (short)size);
}
