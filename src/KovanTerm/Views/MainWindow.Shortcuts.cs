using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using KovanTerm.Interop;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

/// <summary>Keyboard shortcuts and navigation between terminals (see <see cref="Shortcuts"/>, <c>docs/shortcuts.md</c>).</summary>
public partial class MainWindow
{
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
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
        Closed += (_, _) => ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
    }

    // While a terminal is focused, keys go straight to its HWND and never reach WPF KeyBindings.
    // The terminal HWND lives on the UI thread, so the key message is caught here, before the terminal sees it.
    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || (msg.message != WM_KEYDOWN && msg.message != WM_SYSKEYDOWN)) return;
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
                var next = NodeNavigation.Next(_vm.Nodes, n => n.Bounds, _currentNode,
                    forward: action == ShortcutAction.NextTerminal, ViewportCenter());
                if (next != null) GoToNode(next);
                break;
            case ShortcutAction.FitAll:
                if (_vm.Nodes.Count > 0) Editor.FitToScreen();
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
        }
    }

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
        return _vm.Nodes.MinBy(n => (Center(n.Bounds) - c).LengthSquared);
    }

    private static Point Center(Rect r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
}
