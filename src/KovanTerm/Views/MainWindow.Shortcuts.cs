using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using KovanTerm.Interop;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

/// <summary>Klavye kısayolları ve terminaller arası gezinme (bkz. <see cref="Shortcuts"/>, <c>docs/kisayollar.md</c>).</summary>
public partial class MainWindow
{
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    /// <summary>Bir terminale yakınlaşırken terminalin görünür alanın ne kadarını kaplayacağı.</summary>
    private const double ZoomToNodeFill = 0.9;

    private IntPtr _hwnd;

    // Gezinmenin "şu anki" terminali: en son odaklanan ya da kısayolla gidilen. Yalnızca klavye odağına
    // güvenilmez; pencere etkin değilken odak verilemez ve gezinme aynı yerde takılırdı.
    private TerminalNodeViewModel? _currentNode;

    private void InitShortcuts()
    {
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        _canvas.NodeFocused += node => _currentNode = node as TerminalNodeViewModel;
        _vm.Nodes.CollectionChanged += (_, _) =>
        {
            if (_currentNode != null && !_vm.Nodes.Contains(_currentNode)) _currentNode = null;
        };
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
        Closed += (_, _) => ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
    }

    // Terminal odaktayken tuşlar doğrudan terminalin HWND'sine gider ve WPF KeyBinding'lerine ulaşmaz.
    // Terminal HWND'si arayüz iş parçacığında olduğu için tuş mesajı burada, terminalden önce yakalanır.
    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || (msg.message != WM_KEYDOWN && msg.message != WM_SYSKEYDOWN)) return;
        // Yalnızca ana pencere ve içindeki terminaller; açık bir diyalog varsa ona karışma
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
        }
    }

    /// <summary>Görünümü düğüme kaydırır (animasyonlu) ve terminaline odaklanır.</summary>
    private void GoToNode(TerminalNodeViewModel node)
    {
        _currentNode = node;
        Editor.BringIntoView(Center(node.Bounds), true, () => _canvas.FocusNode(node));
    }

    /// <summary>Düğüm görünür alanı dolduracak şekilde yakınlaşır (en fazla %100) ve terminaline odaklanır.</summary>
    private void ZoomToNode(TerminalNodeViewModel node)
    {
        var b = node.Bounds;
        var zoom = Math.Min(Editor.ActualWidth * ZoomToNodeFill / b.Width, Editor.ActualHeight * ZoomToNodeFill / b.Height);
        Editor.ViewportZoom = Math.Clamp(zoom, Editor.MinViewportZoom, 1.0);
        GoToNode(node);
    }

    // Başlığa çift tık: o terminale yakınlaş
    private void NodeHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: TerminalNodeViewModel node }) return;
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
