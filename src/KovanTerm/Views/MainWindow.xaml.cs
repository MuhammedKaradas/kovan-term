using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EasyWindowsTerminalControl;
using KovanTerm.Services;
using KovanTerm.ViewModels;
using Nodify;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly TerminalCanvasController _canvas;
    private Point _contextMenuLocation;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new WorkspaceStore(), new ProfileStore(), new CommandResolver(), new DialogService(this), new SettingsStore());
        DataContext = _vm;
        _canvas = new TerminalCanvasController(Editor);

        // Theme: WPF-UI resources + terminal colors; re-applied when the preference changes
        var theme = new ThemeService(this);
        theme.IsDarkChanged += _canvas.SetDarkTheme;
        Loaded += (_, _) => theme.Apply(_vm.Theme);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Theme)) theme.Apply(_vm.Theme);
        };
        // When a node gets focus, Nodify pans the canvas to it; since terminals get focus on startup and on click,
        // the saved viewport was being lost (#35). The viewport should change only via the user or deliberate commands.
        NodifyEditor.AutoPanOnNodeFocus = false;
        PerfSelfTest.AttachIfRequested(this, Editor, _canvas);
        InitShortcuts();

        // ViewportSize is read-only so it can't be bound; pass it to the ViewModel to center new nodes
        Editor.ViewportUpdated += (_, _) => _vm.ViewportSize = Editor.ViewportSize;

        // Don't leave terminal processes running in the background when a node or the window closes
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

    // Terminal controls are HwndHosts, so they're registered with the canvas controller in the view layer
    private void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is EasyTerminalControl term)
            _canvas.Register(term);
    }

    // Mouse position when the menu opened (canvas coordinates); by the time a menu item is clicked the mouse has moved
    private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e) => _contextMenuLocation = Editor.MouseLocation;

    private void NewTerminalHere_Click(object sender, RoutedEventArgs e) => _vm.NewTerminalCommand.Execute(_contextMenuLocation);

    // The thumb is inside the node, in the layer Nodify scales, so DragDelta values are in canvas units
    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e) => _canvas.BeginInteraction();

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalNodeViewModel node })
            node.ResizeBy(e.HorizontalChange, e.VerticalChange);
        e.Handled = true; // Don't let Nodify drag the node
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e) => _canvas.EndInteraction();
}
