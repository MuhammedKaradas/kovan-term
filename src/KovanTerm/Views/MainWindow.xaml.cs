using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using EasyWindowsTerminalControl;
using KovanTerm.Services;
using KovanTerm.ViewModels;

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
}
