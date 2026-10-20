using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EasyWindowsTerminalControl;
using KovanTerm.Services;
using KovanTerm.ViewModels;
using Nodify;

namespace KovanTerm.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TerminalCanvasController _canvas;
    private Point _contextMenuLocation;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new WorkspaceStore(), new ProfileStore(), new CommandResolver(), new DialogService(this));
        DataContext = _vm;
        _canvas = new TerminalCanvasController(Editor);
        // Düğüm odak alınca Nodify tuvali o düğüme kaydırıyor; terminaller açılışta ve tıklanınca odak aldığı
        // için kaydedilen görünüm bozuluyordu (#35). Görünüm yalnızca kullanıcı ya da bilinçli komutlarla değişmeli.
        NodifyEditor.AutoPanOnNodeFocus = false;
        PerfSelfTest.AttachIfRequested(this, Editor, _canvas);
        InitShortcuts();

        // ViewportSize salt okunur olduğu için bağlanamıyor; yeni düğümü ortalamak için ViewModel'e aktar
        Editor.ViewportUpdated += (_, _) => _vm.ViewportSize = Editor.ViewportSize;

        // Düğüm kapanınca ya da pencere kapanınca terminal işlemleri arkada çalışmaya devam etmesin
        _vm.Nodes.CollectionChanged += OnNodesChanged;
        Closed += (_, _) =>
        {
            _vm.SaveNow();
            _canvas.TerminateAll();
        };
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _canvas.TerminateAll();
            return;
        }
        if (e.OldItems == null) return;
        foreach (var node in e.OldItems)
            _canvas.Terminate(node);
    }

    // Terminal kontrolleri HwndHost olduğu için tuval kontrolcüsüne görünüm katmanında kaydedilir
    private void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is EasyTerminalControl term)
            _canvas.Register(term);
    }

    // Menü açıldığı andaki fare konumu (tuval koordinatı); menü öğesine tıklanınca fare başka yerde olur
    private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e) => _contextMenuLocation = Editor.MouseLocation;

    private void NewTerminalHere_Click(object sender, RoutedEventArgs e) => _vm.NewTerminalCommand.Execute(_contextMenuLocation);

    // Thumb düğümün içinde, Nodify'ın ölçeklediği katmanda olduğu için DragDelta değerleri tuval birimindedir
    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e) => _canvas.BeginInteraction();

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalNodeViewModel node })
            node.ResizeBy(e.HorizontalChange, e.VerticalChange);
        e.Handled = true; // Nodify düğümü sürüklemesin
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e) => _canvas.EndInteraction();
}
