using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KovanTerm.Interop;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

/// <summary>Find in a terminal and clear its scrollback (#141).</summary>
public partial class MainWindow
{
    private readonly TerminalSearch _search = new();
    // Searches run one at a time, off the UI thread (the text pattern answers through the UI thread's messages)
    private Task _searching = Task.CompletedTask;
    // Searching as you type, once typing pauses
    private System.Windows.Threading.DispatcherTimer? _findTimer;
    private TerminalNodeViewModel? _findTyped;

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (NodeOf(sender) is not { IsFinding: true } node) return;
        if (_findTimer == null)
        {
            _findTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _findTimer.Tick += (_, _) =>
            {
                _findTimer.Stop();
                if (_findTyped is { } typed) FindIn(typed, newer: false);
            };
        }
        _findTyped = node;
        _findTimer.Stop();
        _findTimer.Start();
    }

    private void OpenFind(TerminalNodeViewModel? node)
    {
        if (node is not { IsTerminal: true }) return;
        if (node.IsFinding)
        {
            // Already open: Ctrl+Shift+H again goes to the next older match
            FindIn(node, newer: false);
            return;
        }
        node.FindStatus = null;
        node.IsFinding = true;
    }

    private void CloseFind(TerminalNodeViewModel node)
    {
        node.IsFinding = false;
        _canvas.FocusNode(node);
    }

    private void FindIn(TerminalNodeViewModel node, bool newer)
    {
        if (_canvas.WindowOf(node) is not { } hwnd) return;
        var text = node.FindText;
        _searching = _searching.ContinueWith(_ => _search.Find(hwnd, text, newer), TaskScheduler.Default)
            .ContinueWith(t =>
            {
                // Typed on meanwhile: the newer search shows its own result
                if (node.FindText == text) node.FindStatus = text.Length == 0 ? null : t.Result.Text;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        if (newer || text.Length > 0) Services.UsageStats.Count("feature.find");
    }

    private void ClearScrollback(TerminalNodeViewModel? node)
    {
        if (node is not { IsTerminal: true }) return;
        _canvas.ClearScrollback(node);
        Services.UsageStats.Count("feature.clear");
    }

    private static TerminalNodeViewModel? NodeOf(object sender) => (sender as FrameworkElement)?.DataContext as TerminalNodeViewModel;

    private void FindBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible) return;
        box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (NodeOf(sender) is not { } node) return;
        switch (e.Key)
        {
            case Key.Enter:
                // Searched now: the pending as-you-type search would move on to the next match
                _findTimer?.Stop();
                FindIn(node, newer: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;
            case Key.Escape:
                CloseFind(node);
                e.Handled = true;
                break;
        }
    }

    private void FindOlder_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) FindIn(node, newer: false);
    }

    private void FindNewer_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) FindIn(node, newer: true);
    }

    private void FindClose_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) CloseFind(node);
    }
}
