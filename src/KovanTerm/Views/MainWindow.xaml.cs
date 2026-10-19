using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyWindowsTerminalControl;
using KovanTerm.Interop;
using Microsoft.Terminal.Wpf;

namespace KovanTerm.Views;

public partial class MainWindow : Window
{
    public ObservableCollection<TerminalNode> Nodes { get; } = new();

    private const int BaseFontSize = 13;

    private static readonly TerminalTheme DarkTheme = new()
    {
        DefaultBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0x0c, 0x0c, 0x0c)),
        DefaultForeground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0xe6, 0xe8, 0xf0)),
        DefaultSelectionBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0x3a, 0x3c, 0x4a)),
        CursorStyle = CursorStyle.BlinkingBar,
        // Campbell renkleri (Windows Terminal varsayılanı)
        ColorTable = new uint[] { 0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC,
                                  0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2 },
    };

    private readonly List<EasyTerminalControl> _terminals = new();
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _spawnCount;
    private int _appliedFontSize = BaseFontSize;

    // Kaydırma/zoom/sürükleme sırasında canlı terminaller (HWND) yerine anlık görüntüleri gösterilir;
    // hareket bittikten bu kadar sonra canlı terminallere geri dönülür
    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _frozen;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        AddNode("PowerShell", "powershell.exe -NoLogo");
        AddNode("Claude Code", "cmd.exe /k claude");
        AddNode("cmd", "cmd.exe");
        AddNode("PowerShell 2", "powershell.exe -NoLogo");

        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();

        _settleTimer.Tick += (_, _) => { _settleTimer.Stop(); Unfreeze(); };
        Editor.AddHandler(Nodify.ItemContainer.LocationChangedEvent, new RoutedEventHandler((_, _) => OnCanvasMoving()));
    }

    private void AddNode(string title, string commandLine)
    {
        // 2 sütunlu ızgaraya diz
        var i = _spawnCount++;
        Nodes.Add(new TerminalNode
        {
            Title = title,
            CommandLine = commandLine,
            Location = new Point(40 + (i % 2) * 600, 40 + (i / 2) * 400),
        });
    }

    private void AddPowerShell_Click(object sender, RoutedEventArgs e) => AddNode("PowerShell", "powershell.exe -NoLogo");
    private void AddClaude_Click(object sender, RoutedEventArgs e) => AddNode("Claude Code", "cmd.exe /k claude");
    private void AddCmd_Click(object sender, RoutedEventArgs e) => AddNode("cmd", "cmd.exe");

    private void CloseNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TerminalNode node })
            Nodes.Remove(node);
    }

    private void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not EasyTerminalControl term || _terminals.Contains(term)) return;
        _terminals.Add(term);
        term.Unloaded += (_, _) => _terminals.Remove(term);
        // Terminal hazır olunca mevcut zoom'a uygun yazı boyutunu uygula
        Dispatcher.BeginInvoke(() => SetFont(term, _appliedFontSize), DispatcherPriority.ApplicationIdle);
    }

    private void Editor_ViewportUpdated(object sender, RoutedEventArgs e) => OnCanvasMoving();

    private void OnCanvasMoving()
    {
        if (!IsLoaded) return;
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
        Dispatcher.BeginInvoke(() =>
        {
            if (_frozen) return;
            foreach (var term in _terminals)
                if (GetSnapshotImage(term) is { } image) { image.Visibility = Visibility.Collapsed; image.Source = null; }
        }, DispatcherPriority.Background);
    }

    private static Image? GetSnapshotImage(EasyTerminalControl term) =>
        (term.Parent as Grid)?.Children.OfType<Image>().FirstOrDefault();

    // HWND, WPF zoom'uyla ölçeklenmediği için yazı boyutunu zoom'a göre ayarla
    private void ApplyZoomToTerminals()
    {
        var size = Math.Max(4, (int)Math.Round(BaseFontSize * Editor.ViewportZoom));
        if (size == _appliedFontSize) return;
        _appliedFontSize = size;
        foreach (var term in _terminals)
            SetFont(term, size);
    }

    private static void SetFont(EasyTerminalControl term, int size) =>
        term.Terminal?.SetTheme(DarkTheme, "Cascadia Code", (short)size);

    private void UpdateStats()
    {
        using var self = Process.GetCurrentProcess();
        var mb = self.WorkingSet64 / 1024 / 1024;
        StatsText.Text = $"Uygulama RAM: {mb} MB   |   Terminal: {Nodes.Count}   |   Zoom: {Editor.ViewportZoom:0.00}";
    }
}
