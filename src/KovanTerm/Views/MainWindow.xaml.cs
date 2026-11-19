using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KovanTerm.Interop;
using KovanTerm.Services;
using KovanTerm.Services.Dictation;
using KovanTerm.Terminals;
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
        var resolver = new CommandResolver();
        _vm = new MainViewModel(new WorkspaceStore(), new ProfileStore(), resolver, new WslLauncher(resolver), new DialogService(this),
            new SettingsStore(), _hookServer, usage: new ClaudeUsage(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) }),
            dictation: CreateDictation(),
            pullRequests: resolver.Resolve("gh") is { } gh ? new PullRequests(gh) : null);
        DataContext = _vm;
        _canvas = new TerminalCanvasController(Editor);
        _vm.TerminalInputRequested += _canvas.SendInput;
        _canvas.MotionChanged += OnCanvasMotion;
        _canvas.AnswerKeyTyped += node => _vm.OnAnswerKeyTyped((TerminalNodeViewModel)node);
        _canvas.ProgramStatusReported += (node, reports) => _vm.OnProgramStatus((TerminalNodeViewModel)node, reports);
        _canvas.WatchScreen = node => _vm.WatchesScreen((TerminalNodeViewModel)node);
        _canvas.LocalUrlPrinted += (node, url) => _vm.OnLocalUrlPrinted((TerminalNodeViewModel)node, url);
        _vm.Nodes.CollectionChanged += (_, e) =>
        {
            if (e.OldItems == null) return;
            foreach (TerminalNodeViewModel n in e.OldItems) _previews.Remove(n);
        };
        _canvas.ScreenRead += (node, text) => _vm.OnScreenRead((TerminalNodeViewModel)node, text);
        _vm.AttentionNeeded += _ => FlashIfInactive();
        InitNotifications();

        // Theme: WPF-UI resources + terminal colors; re-applied when the preference changes
        var theme = new ThemeService(this);
        theme.IsDarkChanged += _canvas.SetDarkTheme;
        Loaded += (_, _) => theme.Apply(_vm.Theme);
        // Once the window is up (the question is a dialog over it)
        ContentRendered += async (_, _) => await _vm.CheckOrphanSessionsAsync();
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
        _vm.WorkspaceSwitched += () => Dispatcher.BeginInvoke(_canvas.RefreshVisibility, System.Windows.Threading.DispatcherPriority.Loaded);
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
        // Notes aren't agents
        view.Filter = n => n is TerminalNodeViewModel { IsNote: false, IsGroup: false };
        view.LiveSortingProperties.Add(nameof(TerminalNodeViewModel.StatusOrder));
        view.IsLiveSorting = true;
        return view;
    }

    // A row in the Agents panel: go to that terminal (but not when Allow/Deny inside the row was clicked)
    private void AgentRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) != null) return;
        if (sender is FrameworkElement { DataContext: TerminalNodeViewModel node }) _vm.GoToNodeCommand.Execute(node);
    }

    // ---- Workspace tabs ----

    // Double-click a tab: rename it
    private void WorkspaceTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: WorkspaceViewModel workspace }) return;
        workspace.IsRenaming = true;
        e.Handled = true;
    }

    private void RenameWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceViewModel workspace }) workspace.IsRenaming = true;
    }

    private void CloseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceViewModel workspace }) _vm.CloseWorkspaceCommand.Execute(workspace);
    }

    private void WorkspaceName_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not System.Windows.Controls.TextBox box) return;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void WorkspaceName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        Keyboard.ClearFocus();
        Editor.Focus();
        e.Handled = true;
    }

    private void WorkspaceName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceViewModel { IsRenaming: true } workspace }) _vm.FinishRenaming(workspace);
    }

    // Right-click a node's header (or a group's): move it to another workspace
    private void NodeMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TerminalNodeViewModel node, ContextMenu: { } menu }) return;
        menu.Items.Clear();
        var move = new System.Windows.Controls.MenuItem
        {
            Header = node.IsGroup ? "Move group to workspace" : "Move to workspace",
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.TabDesktopArrowClockwise24 },
        };
        foreach (var workspace in _vm.OtherWorkspaces(node))
        {
            var item = new System.Windows.Controls.MenuItem { Header = workspace.Name };
            item.Click += (_, _) => _vm.MoveToWorkspace(node, workspace);
            move.Items.Add(item);
        }
        if (move.Items.Count > 0) move.Items.Add(new Separator());
        var fresh = new System.Windows.Controls.MenuItem { Header = "New workspace" };
        fresh.Click += (_, _) => _vm.MoveToNewWorkspace(node);
        move.Items.Add(fresh);
        menu.Items.Add(move);
    }

    // ---- Web preview nodes ----

    private readonly Dictionary<TerminalNodeViewModel, Microsoft.Web.WebView2.Wpf.WebView2> _previews = new();

    private async void Preview_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border { DataContext: TerminalNodeViewModel node, Child: Microsoft.Web.WebView2.Wpf.WebView2 view } host
            || _previews.ContainsKey(node)) return;
        _previews[node] = view;
        _canvas.RegisterPreview(host, view);
        try
        {
            await view.EnsureCoreWebView2Async(await PreviewEnvironment.GetAsync());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                       or Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            node.Url = $"(WebView2 isn't available: {ex.Message})";
            return;
        }
        view.CoreWebView2.NavigationCompleted += (_, _) => _canvas.PreviewChanged(node);
        // Links that open a new window stay in the preview
        view.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (LocalUrls.Normalize(args.Uri) is { } uri) view.CoreWebView2.Navigate(uri.AbsoluteUri);
        };
        view.SourceChanged += (_, _) =>
        {
            if (view.Source is { } source && source.Scheme is "http" or "https") node.Url = source.AbsoluteUri;
        };
        if (LocalUrls.Normalize(node.Url) is { } start) view.CoreWebView2.Navigate(start.AbsoluteUri);
    }

    // Enter in the address bar: go there
    private void PreviewAddress_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not FrameworkElement { DataContext: TerminalNodeViewModel node } box) return;
        e.Handled = true;
        var text = (box as System.Windows.Controls.TextBox)?.Text;
        if (LocalUrls.Normalize(text) is not { } uri || !_previews.TryGetValue(node, out var view) || view.CoreWebView2 == null) return;
        node.Url = uri.AbsoluteUri;
        view.CoreWebView2.Navigate(uri.AbsoluteUri);
    }

    private void PreviewReload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalNodeViewModel node } && _previews.TryGetValue(node, out var view))
            view.CoreWebView2?.Reload();
    }

    private void PreviewOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalNodeViewModel node } && LocalUrls.Normalize(node.Url) is { } uri)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void NewPreviewHere_Click(object sender, RoutedEventArgs e) => _vm.NewPreviewCommand.Execute(_contextMenuLocation);

    private void NewNoteHere_Click(object sender, RoutedEventArgs e) => _vm.NewNoteCommand.Execute(_contextMenuLocation);

    /// <summary>"Send to…" on a note: a menu of the terminals to paste it into.</summary>
    private void NoteSend_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TerminalNodeViewModel note } button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var empty = note.NoteText.Trim().Length == 0;
        foreach (var target in _vm.NoteTargets)
        {
            var item = new System.Windows.Controls.MenuItem { Header = target.Title, IsEnabled = !empty };
            item.Click += (_, _) => _vm.SendNote(note, target);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "No terminal to send to", IsEnabled = false });
        menu.IsOpen = true;
    }

    // While the canvas moves, notes are drawn from a bitmap: re-rendering their text at every zoom step dropped
    // zooming from ~59 to ~47 fps (KOVAN_PERF_SELFTEST, 8 terminals + 3 notes)
    private readonly HashSet<FrameworkElement> _noteBodies = [];

    private void NoteBody_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement body) _noteBodies.Add(body);
    }

    private void NoteBody_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement body) _noteBodies.Remove(body);
    }

    private void OnCanvasMotion(bool moving)
    {
        // Cached at the current zoom (at least 1:1), so it looks the same until the motion ends
        var cache = moving ? new System.Windows.Media.BitmapCache(Math.Max(1, Editor.ViewportZoom)) : null;
        foreach (var body in _noteBodies) body.CacheMode = cache;
    }

    /// <summary>Editing starts with the cursor in the note.</summary>
    private void NoteEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is System.Windows.Controls.TextBox box)
            Dispatcher.BeginInvoke(() => { box.Focus(); box.CaretIndex = box.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---- Board cards ----

    private Point? _cardPressedAt;

    private void TaskCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _cardPressedAt = e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) != null ? null : e.GetPosition(this);

    // Dragging starts once the mouse moved a few pixels with the button down (not on the card's buttons)
    private void TaskCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _cardPressedAt is not { } start
            || sender is not FrameworkElement { DataContext: TaskCard card } element) return;
        var moved = e.GetPosition(this) - start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _cardPressedAt = null;
        DragDrop.DoDragDrop(element, new DataObject(typeof(TaskCard), card), DragDropEffects.Move);
    }

    private void TaskColumn_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TaskCard)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void TaskColumn_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string state } && e.Data.GetData(typeof(TaskCard)) is TaskCard card)
            _vm.BoardPanel.Drop(card, state);
        e.Handled = true;
    }

    // "Give to an agent": a menu of the agents on the canvas
    private void AssignTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskCard card } button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var agent in _vm.TaskAgents())
        {
            var item = new System.Windows.Controls.MenuItem { Header = MainViewModel.MeshName(agent) };
            item.Click += (_, _) => _vm.AssignTask(card, agent);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "No agents on the canvas", IsEnabled = false });
        menu.IsOpen = true;
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

    private DictationViewModel CreateDictation()
    {
        // KOVAN_DICTATION_WAV=<file>: "records" that file instead of the microphone (testing without speaking)
        IAudioRecorder recorder = Environment.GetEnvironmentVariable("KOVAN_DICTATION_WAV") is { Length: > 0 } wav
            ? new WavFileRecorder(wav)
            : new WaveInRecorder();
        return new DictationViewModel(recorder, new SpeechModelStore(), path => new WhisperSpeechToText(path),
            new DialogService(this), new SettingsStore(), MainViewModel.StatusLog);
    }

    // Terminal controls are HwndHosts, so they're registered with the canvas controller in the view layer
    private void Terminal_Ready(object sender, RoutedEventArgs e)
    {
        if (sender is TerminalHost host)
            _canvas.Register(host);
    }

    // Mouse position when the menu opened (canvas coordinates); by the time a menu item is clicked the mouse has moved
    private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _contextMenuLocation = Editor.MouseLocation;
        GroupSelectionItem.Visibility = SelectedNodes().Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<TerminalNodeViewModel> SelectedNodes() =>
        Editor.SelectedItems?.OfType<TerminalNodeViewModel>().Where(n => !n.IsGroup).ToList() ?? [];

    private void GroupSelection_Click(object sender, RoutedEventArgs e) => _vm.AddGroup(_contextMenuLocation, SelectedNodes());

    private void NewGroupHere_Click(object sender, RoutedEventArgs e) => _vm.AddGroup(_contextMenuLocation, []);

    // Enter/Esc finish renaming a group
    private void GroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        Keyboard.ClearFocus();
        Editor.Focus();
        e.Handled = true;
    }

    // The selected nodes are checked in the dialog; with none selected, this workspace's agents
    private void Broadcast_Click(object sender, RoutedEventArgs e) => _ = _vm.BroadcastAsync(SelectedNodes());

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
