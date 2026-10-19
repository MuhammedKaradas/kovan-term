using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
/// <item>Görüntüleri boştayken arka planda önbelleğe alır; böylece harekete geçiş anlık olur.</item>
/// <item>HWND, WPF zoom'uyla ölçeklenmediği için yazı boyutunu zoom'a göre ayarlar.</item>
/// <item>Düğüm kapanınca terminalin işlem ağacını sonlandırır.</item>
/// </list>
/// Terminal kontrolünün, kardeşi bir <see cref="Image"/> olan bir <see cref="Grid"/> içinde durması beklenir.
/// </summary>
public sealed class TerminalCanvasController
{
    private const int BaseFontSize = 13;
    private const int MinFontSize = 4;

    /// <summary>Tuvaldeki bir terminal ve önbellekteki görüntüsü.</summary>
    private sealed class Entry(EasyTerminalControl control)
    {
        public EasyTerminalControl Control { get; } = control;
        /// <summary>Son görüntüden sonra terminal çıktısı değişti mi? Okuma iş parçacığından yazılır.</summary>
        public volatile bool Dirty = true;
        public BitmapSource? Snapshot;
    }

    private readonly NodifyEditor _editor;
    // Anahtar: terminalin DataContext'i (düğüm ViewModel'i). Görsel ağaçtan bağımsız
    // tutulur ki düğüm listeden çıkarıldığında terminal hâlâ bulunabilsin.
    private readonly Dictionary<object, Entry> _byNode = new();

    // Hareket bittikten bu kadar sonra canlı terminallere geri dönülür
    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _frozen;
    // Boyutlandırma gibi süresi belli olmayan etkileşimlerde, etkileşim bitene kadar fotoğraf modunda kal
    private bool _held;
    private readonly DispatcherTimer _snapshotCleanupTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _appliedFontSize = BaseFontSize;

    // Önbellek: çıktısı değişen terminallerin görüntüsü arka planda yenilenir.
    // PrintWindow ~bir DWM karesi bekler ama arayüz iş parçacığını meşgul etmez.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _refreshInFlight;

    /// <summary>Fotoğraf moduna giriş/çıkış süreleri (ölçüm için; bkz. <see cref="PerfSelfTest"/>).</summary>
    public event Action<string, TimeSpan>? Timing;

    public TerminalCanvasController(NodifyEditor editor)
    {
        _editor = editor;
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            if (!_held) Unfreeze();
        };
        _snapshotCleanupTimer.Tick += (_, _) => RemoveSnapshots();
        _refreshTimer.Tick += (_, _) => RefreshDirtySnapshots();
        _refreshTimer.Start();
        _editor.ViewportUpdated += (_, _) => OnCanvasMoving();
        _editor.AddHandler(ItemContainer.LocationChangedEvent, new RoutedEventHandler((_, _) => OnCanvasMoving()));
    }

    public void Register(EasyTerminalControl term)
    {
        if (term.DataContext is not { } node || _byNode.ContainsKey(node)) return;
        var entry = new Entry(term);
        _byNode[node] = entry;
        // Çıktı geldikçe görüntü eskir; boyut değişince de (boyutlandırma, yazı boyutu) yeniden alınmalı
        if (term.ConPTYTerm != null)
            term.ConPTYTerm.TerminalOutput += (_, _) => entry.Dirty = true;
        term.SizeChanged += (_, _) => entry.Dirty = true;
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
        if (!_byNode.Remove(node, out var entry)) return;
        KillProcessTree(entry.Control);
    }

    public void TerminateAll()
    {
        _refreshTimer.Stop();
        foreach (var entry in _byNode.Values)
            KillProcessTree(entry.Control);
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

    /// <summary>Etkileşim boyunca (ör. boyutlandırma) terminalleri anlık görüntüyle değiştirir.</summary>
    public void BeginInteraction()
    {
        _held = true;
        Freeze();
    }

    /// <summary>Etkileşim bitti; kısa bir süre sonra canlı terminallere dönülür.</summary>
    public void EndInteraction()
    {
        _held = false;
        _settleTimer.Stop();
        _settleTimer.Start();
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
        _snapshotCleanupTimer.Stop();
        var sw = Stopwatch.StartNew();
        int fromCache = 0, captured = 0;
        foreach (var entry in _byNode.Values)
        {
            if (GetSnapshotImage(entry.Control) is not { } image) continue;
            // Önbellekteki görüntü güncelse onu kullan; değilse (ör. hiç alınmamış) şimdi al
            var shot = !entry.Dirty && entry.Snapshot != null ? entry.Snapshot : null;
            if (shot != null) fromCache++;
            else if (TerminalSnapshot.GetTarget(entry.Control) is { } target)
            {
                shot = entry.Snapshot = TerminalSnapshot.Capture(target, TerminalThemes.DarkBackground);
                entry.Dirty = false;
                captured++;
            }
            // Eski de olsa bir görüntü, hiç görüntü olmamasından iyidir
            shot ??= entry.Snapshot;
            if (shot == null) continue;
            image.Source = shot;
            image.Visibility = Visibility.Visible;
            entry.Control.Visibility = Visibility.Hidden;
        }
        Timing?.Invoke($"fotoğraf moduna giriş ({_byNode.Count} terminal: {fromCache} önbellekten, {captured} anında)", sw.Elapsed);
    }

    private void Unfreeze()
    {
        if (!_frozen) return;
        _frozen = false;
        var sw = Stopwatch.StartNew();
        ApplyZoomToTerminals();
        foreach (var entry in _byNode.Values)
        {
            entry.Control.Visibility = Visibility.Visible;
            // Konum/yazı boyutu değişmiş olabilir; görüntü yeniden alınmalı
            entry.Dirty = true;
        }
        Timing?.Invoke($"canlı terminale dönüş ({_byNode.Count} terminal)", sw.Elapsed);
        // Terminal (özellikle yazı boyutu değiştiyse) ilk karesini çizene kadar görüntü arkada kalsın;
        // hemen kaldırılırsa kısa bir boş kare görünüyor
        _snapshotCleanupTimer.Start();
    }

    private void RemoveSnapshots()
    {
        _snapshotCleanupTimer.Stop();
        if (_frozen) return;
        foreach (var entry in _byNode.Values)
            if (GetSnapshotImage(entry.Control) is { } image) { image.Visibility = Visibility.Collapsed; image.Source = null; }
    }

    /// <summary>
    /// Çıktısı değişmiş tüm görünür terminallerin görüntüsünü tek bir arka plan turunda yeniler.
    /// PrintWindow çağrıları sıraya girdiği için (terminal başına ~15 ms) arka planda sırayla alınır;
    /// arayüz iş parçacığı yalnızca hedef bilgisini toplarken kısa bir süre kullanılır.
    /// </summary>
    private void RefreshDirtySnapshots()
    {
        if (_frozen || _refreshInFlight) return;
        var batch = new List<(Entry Entry, TerminalSnapshot.Target Target)>();
        foreach (var entry in _byNode.Values)
        {
            if (!entry.Dirty || entry.Control.Visibility != Visibility.Visible) continue;
            if (TerminalSnapshot.GetTarget(entry.Control) is not { } target) continue;
            // Görüntü alınırken gelen çıktı tekrar işaretlesin diye bayrağı önce indir
            entry.Dirty = false;
            batch.Add((entry, target));
        }
        if (batch.Count == 0) return;

        _refreshInFlight = true;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(() => batch.Select(b => TerminalSnapshot.Capture(b.Target, TerminalThemes.DarkBackground)).ToList())
            .ContinueWith(t =>
            {
                _refreshInFlight = false;
                for (var i = 0; i < batch.Count; i++)
                {
                    if (t.Result[i] is { } shot) batch[i].Entry.Snapshot = shot;
                    else batch[i].Entry.Dirty = true;
                }
            }, ui);
    }

    private static Image? GetSnapshotImage(EasyTerminalControl term) =>
        (term.Parent as Grid)?.Children.OfType<Image>().FirstOrDefault();

    private void ApplyZoomToTerminals()
    {
        var size = FontSizeForZoom(_editor.ViewportZoom);
        if (size == _appliedFontSize) return;
        _appliedFontSize = size;
        foreach (var entry in _byNode.Values)
            SetFont(entry.Control, size);
    }

    private static int FontSizeForZoom(double zoom) => Math.Max(MinFontSize, (int)Math.Round(BaseFontSize * zoom));

    private static void SetFont(EasyTerminalControl term, int size) =>
        term.Terminal?.SetTheme(TerminalThemes.Dark, "Cascadia Code", (short)size);
}
