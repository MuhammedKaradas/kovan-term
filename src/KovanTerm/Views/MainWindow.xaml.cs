using System.Collections.Specialized;
using System.Windows;
using EasyWindowsTerminalControl;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

public partial class MainWindow : Window
{
    private readonly TerminalCanvasController _canvas;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        _canvas = new TerminalCanvasController(Editor);

        // Düğüm kapanınca ya da pencere kapanınca terminal işlemleri arkada çalışmaya devam etmesin
        vm.Nodes.CollectionChanged += OnNodesChanged;
        Closed += (_, _) => _canvas.TerminateAll();
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
}
