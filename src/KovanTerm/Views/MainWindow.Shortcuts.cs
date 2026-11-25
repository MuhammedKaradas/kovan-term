using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using KovanTerm.Interop;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

/// <summary>Keyboard shortcuts and navigation between terminals (see <see cref="Shortcuts"/>, <c>docs/shortcuts.md</c>).</summary>
public partial class MainWindow
{
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, MK_CONTROL = 0x0008;
    /// <summary>The Ctrl+click whose press was taken from the terminal: its release is taken too.</summary>
    private bool _takingLinkClick;

    /// <summary>
    /// With web links set to open in a preview, a Ctrl+click in a terminal is taken before the terminal sees it (it
    /// would open the default browser), the link under it is read from the terminal's text, and a web link opens in a
    /// preview next to the terminal. Other links (file, mailto) go to the system, as the terminal would send them.
    /// </summary>
    private bool OnTerminalClick(MSG msg)
    {
        if (msg.message == WM_LBUTTONUP)
        {
            if (!_takingLinkClick) return false;
            _takingLinkClick = false;
            return true;
        }
        if (((long)msg.wParam & MK_CONTROL) == 0 || _canvas.NodeOfWindow(msg.hwnd) is not TerminalNodeViewModel node) return false;
        if (!_vm.Settings.OpenWebLinksInPreview) return false;
        _takingLinkClick = true;
        var lParam = (long)msg.lParam;
        var point = new Native.POINT { X = (short)(lParam & 0xFFFF), Y = (short)((lParam >> 16) & 0xFFFF) };
        Native.ClientToScreen(msg.hwnd, ref point);
        var hwnd = msg.hwnd;
        Task.Run(() => TerminalLinkReader.LinkAt(hwnd, new Point(point.X, point.Y))).ContinueWith(t =>
        {
            if (t.Result is not { } link) return;
            if (Services.TerminalLinks.IsWeb(link)) _vm.OpenLinkInPreview(node, link);
            else
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { }
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return true;
    }
    /// <summary>How much of the viewport a terminal fills when zooming to it.</summary>
    private const double ZoomToNodeFill = 0.9;

    private IntPtr _hwnd;

    // The "current" terminal for navigation: the last one focused or navigated to via shortcut. Keyboard focus
    // alone isn't reliable; focus can't be set while the window is inactive, so navigation would get stuck.
    private TerminalNodeViewModel? _currentNode
    {
        get => _vm.CurrentNode;
        set => _vm.CurrentNode = value;
    }

    private void InitShortcuts()
    {
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        // Until the user picks one, the terminal in the middle of the restored view is the current one
        ContentRendered += (_, _) => _currentNode ??= NearestToCenter();
        _canvas.NodeFocused += node => _currentNode = node as TerminalNodeViewModel;
        _vm.Nodes.CollectionChanged += (_, _) =>
        {
            if (_currentNode != null && !_vm.Nodes.Contains(_currentNode)) _currentNode = null;
        };
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
        InitVoiceCommands();
        Closed += (_, _) => ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
    }

    // While a terminal is focused, keys go straight to its HWND and never reach WPF KeyBindings.
    // The terminal HWND lives on the UI thread, so the key message is caught here, before the terminal sees it.
    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled) return;
        if (msg.message is WM_LBUTTONDOWN or WM_LBUTTONUP)
        {
            handled = OnTerminalClick(msg);
            return;
        }
        if (msg.message != WM_KEYDOWN && msg.message != WM_SYSKEYDOWN) return;
        // Only the main window and its terminals; don't interfere with an open dialog
        if (_hwnd == IntPtr.Zero || (msg.hwnd != _hwnd && !Native.IsChild(_hwnd, msg.hwnd))) return;
        var key = KeyInterop.KeyFromVirtualKey((int)msg.wParam);
        if (Shortcuts.Match(key, Keyboard.Modifiers) is not { } action) return;
        handled = true;
        RunShortcut(action);
    }

    internal void RunShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.NextTerminal:
            case ShortcutAction.PreviousTerminal:
                var next = NodeNavigation.Next(VisibleNodes().ToList(), n => n.Bounds, _currentNode,
                    forward: action == ShortcutAction.NextTerminal, ViewportCenter());
                if (next != null) GoToNode(next);
                break;
            case ShortcutAction.FitAll:
                // Only this workspace's nodes: the hidden ones are still on the canvas
                if (VisibleNodes().Select(n => n.Bounds).Aggregate(Rect.Empty, Rect.Union) is { IsEmpty: false } all)
                    Editor.FitToScreen(all);
                break;
            case ShortcutAction.ZoomToFocused:
                if ((_currentNode ?? NearestToCenter()) is { } node)
                    ZoomToNode(node);
                break;
            case ShortcutAction.NewTerminal:
                _vm.NewTerminalCommand.Execute(null);
                break;
            case ShortcutAction.ToggleAgentsPanel:
                _vm.ToggleAgentsPanelCommand.Execute(null);
                break;
            case ShortcutAction.ToggleChangesPanel:
                _vm.ToggleChangesPanelCommand.Execute(null);
                break;
            case ShortcutAction.RecentConversations:
                _vm.OpenRecentConversationCommand.Execute(null);
                break;
            case ShortcutAction.ToggleBoard:
                _vm.ToggleBoardCommand.Execute(null);
                break;
            case ShortcutAction.NewNote:
                _vm.NewNoteCommand.Execute(null);
                break;
            case ShortcutAction.NextWorkspace:
                _vm.NextWorkspaceCommand.Execute(null);
                break;
            case ShortcutAction.PreviousWorkspace:
                _vm.PreviousWorkspaceCommand.Execute(null);
                break;
            case ShortcutAction.Settings:
                ShowSettings();
                break;
            case ShortcutAction.Statistics:
                StatisticsWindow.Show(this);
                break;
            case ShortcutAction.Broadcast:
                _ = _vm.BroadcastAsync(SelectedNodes());
                break;
            case ShortcutAction.Dictate:
                ToggleDictation();
                break;
            case ShortcutAction.VoiceCommand:
                ToggleVoiceCommand();
                break;
            case ShortcutAction.Find:
                OpenFind(_currentNode);
                break;
            case ShortcutAction.ClearScrollback:
                ClearScrollback(_currentNode);
                break;
        }
    }

    /// <summary>Starts dictating into the current terminal, or stops and types what was said.</summary>
    private async void ToggleDictation()
    {
        if (_vm.Dictation is not { } dictation) return;
        var node = _currentNode is { IsTerminal: true } current ? current : null;
        await dictation.ToggleAsync(node, node?.Title);
    }

    private void Dictate_Click(object sender, RoutedEventArgs e) => ToggleDictation();

    /// <summary>Starts listening for a voice command, or stops and carries out what was said.</summary>
    private async void ToggleVoiceCommand()
    {
        if (_vm.Dictation is { } dictation) await dictation.ToggleCommandAsync();
    }

    private void VoiceCommand_Click(object sender, RoutedEventArgs e) => ToggleVoiceCommand();

    private void InitVoiceCommands()
    {
        if (_vm.Dictation is not { } dictation) return;
        dictation.CommandVocabulary = () => _vm.Nodes.Where(n => !n.IsGroup).Select(n => n.Title)
            .Concat(_vm.Workspaces.Select(w => w.Name));
        dictation.CommandHeard += async heard =>
        {
            if (await _vm.RunVoiceCommandAsync(heard) is { Length: > 0 } result) dictation.Report(result);
        };
        _vm.ViewCommandRequested += (kind, node) =>
        {
            switch (kind)
            {
                case Services.Voice.VoiceCommandKind.FitAll:
                    RunShortcut(ShortcutAction.FitAll);
                    break;
                case Services.Voice.VoiceCommandKind.Zoom when node != null:
                    ZoomToNode(node);
                    break;
            }
        };
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowSettings() => SettingsWindow.Show(this, () =>
    {
        var settings = new SettingsViewModel(new Services.SettingsStore(), theme => _vm.Theme = theme, _vm.SetOutsideSessions);
        settings.Saved += UpdateStatusLight;
        settings.Saved += ApplyTerminalSchemes;
        return settings;
    });

    /// <summary>Pans the viewport to the node (animated) and focuses its terminal.</summary>
    private void GoToNode(TerminalNodeViewModel node)
    {
        _currentNode = node;
        Editor.BringIntoView(Center(node.Bounds), true, () => _canvas.FocusNode(node));
    }

    /// <summary>Zooms so the node fills the viewport (at most 100%) and focuses its terminal.</summary>
    private void ZoomToNode(TerminalNodeViewModel node)
    {
        var b = node.Bounds;
        var zoom = Math.Min(Editor.ActualWidth * ZoomToNodeFill / b.Width, Editor.ActualHeight * ZoomToNodeFill / b.Height);
        Editor.ViewportZoom = Math.Clamp(zoom, Editor.MinViewportZoom, 1.0);
        GoToNode(node);
    }

    // Click on the header: that terminal becomes the current one (Changes panel, navigation).
    // Double-click: zoom to it.
    private void NodeHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TerminalNodeViewModel node }) return;
        _currentNode = node;
        if (e.ClickCount != 2) return;
        ZoomToNode(node);
        e.Handled = true;
    }

    private Point ViewportCenter() =>
        Editor.ViewportLocation + new Vector(Editor.ViewportSize.Width / 2, Editor.ViewportSize.Height / 2);

    private TerminalNodeViewModel? NearestToCenter()
    {
        var c = ViewportCenter();
        return VisibleNodes().MinBy(n => (Center(n.Bounds) - c).LengthSquared);
    }

    /// <summary>Nodes on the workspace being shown, without group frames.</summary>
    private IEnumerable<TerminalNodeViewModel> VisibleNodes() => _vm.Nodes.Where(n => !n.IsGroup && n.IsInActiveWorkspace);

    private static Point Center(Rect r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
}
