using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EasyWindowsTerminalControl;
using KovanTerm.Interop;
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
        // Before any terminal starts: they inherit this process's environment
        AgentEnvironment.RemoveSessionMarkers();
        _vm = new MainViewModel(new WorkspaceStore(), new ProfileStore(), new CommandResolver(), new DialogService(this), new SettingsStore(),
            _hookServer, usage: new ClaudeUsage(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) }));
        DataContext = _vm;
        _canvas = new TerminalCanvasController(Editor);
        _vm.TerminalInputRequested += _canvas.SendInput;
        _canvas.AnswerKeyTyped += node => _vm.OnAnswerKeyTyped((TerminalNodeViewModel)node);
        _canvas.ProgramStatusReported += (node, reports) => _vm.OnProgramStatus((TerminalNodeViewModel)node, reports);
        _canvas.WatchScreen = node => _vm.WatchesScreen((TerminalNodeViewModel)node);
        _canvas.ScreenRead += (node, text) => _vm.OnScreenRead((TerminalNodeViewModel)node, text);
        _vm.AttentionNeeded += _ => FlashIfInactive();

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
        _vm.GoToNodeRequested += GoToNode;
        AgentsList.ItemsSource = CreateAgentsView(_vm.Nodes);

        // ViewportSize is read-only so it can't be bound; pass it to the ViewModel to center new nodes
        Editor.ViewportUpdated += (_, _) => _vm.ViewportSize = Editor.ViewportSize;

        // Don't leave terminal processes running in the background when a node or the window closes
        _vm.Nodes.CollectionChanged += OnNodesChanged;
        Closed += (_, _) =>
        {
            _vm.SaveNow();
            _canvas.TerminateAll();
            _hookServer.Dispose();
        };
    }

    private readonly AgentHookServer _hookServer = new();

    /// <summary>
    /// Agents panel list: grouped by working folder; within a group, agents waiting for the user first, then working
    /// ones, re-sorted live as statuses change.
    /// </summary>
    private static ListCollectionView CreateAgentsView(IList nodes)
    {
        var view = new ListCollectionView(nodes);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TerminalNodeViewModel.GroupName)));
        view.SortDescriptions.Add(new SortDescription(nameof(TerminalNodeViewModel.GroupName), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(TerminalNodeViewModel.StatusOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(TerminalNodeViewModel.Title), ListSortDirection.Ascending));
        view.LiveSortingProperties.Add(nameof(TerminalNodeViewModel.StatusOrder));
        view.IsLiveSorting = true;
        return view;
    }

    // A row in the Agents panel: go to that terminal (but not when Allow/Deny inside the row was clicked)
    private void AgentRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) != null) return;
        if (sender is FrameworkElement { DataContext: TerminalNodeViewModel node }) GoToNode(node);
    }

    // Board: Enter in the new task box adds it
    private void NewTask_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !_vm.BoardPanel.AddTaskCommand.CanExecute(null)) return;
        _vm.BoardPanel.AddTaskCommand.Execute(null);
        e.Handled = true;
    }

    // Changes panel: click a diff line to write a note for it
    private void DiffLine_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DiffLine line } || !line.CanAnnotate) return;
        _vm.Changes.StartNoteCommand.Execute(line);
        Dispatcher.BeginInvoke(() => NoteBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void NoteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _vm.Changes.AddNoteCommand.Execute(null);
        else if (e.Key == Key.Escape) _vm.Changes.CancelNoteCommand.Execute(null);
        else return;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T match) return match;
        return null;
    }

    /// <summary>Flashes the taskbar button until the window is activated; nothing if it's already in front.</summary>
    private void FlashIfInactive()
    {
        if (IsActive || _hwnd == IntPtr.Zero) return;
        var info = new Native.FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.FLASHWINFO>(),
            hwnd = _hwnd,
            dwFlags = Native.FLASHW_TRAY | Native.FLASHW_TIMERNOFG,
        };
        Native.FlashWindowEx(ref info);
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

    private void RecentConversationHere_Click(object sender, RoutedEventArgs e) => _vm.OpenRecentConversationCommand.Execute(_contextMenuLocation);

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
