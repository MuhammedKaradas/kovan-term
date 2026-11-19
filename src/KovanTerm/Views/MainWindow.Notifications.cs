using System.Windows;
using KovanTerm.Interop;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

/// <summary>Windows notifications while Kovan is in the background (#80).</summary>
public partial class MainWindow
{
    private TrayNotifier? _notifier;
    private readonly AgentNotifications _notices = new();
    private TerminalNodeViewModel? _noticeTarget;

    private void InitNotifications()
    {
        _vm.AttentionNeeded += node => Notify(n => n.Waiting(node));
        _vm.TurnFinished += node => Notify(n => n.Finished(node));
        // Back in Kovan: everything pending has been seen, and the icon goes with its notifications
        Activated += (_, _) =>
        {
            _notices.Clear();
            _notifier?.Hide();
        };
        Closed += (_, _) => _notifier?.Dispose();

        // Agents working: Windows doesn't sleep on its own (a long task left running isn't cut off halfway)
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.WorkingCount)) UpdateSleepBlock();
        };
        Closed += (_, _) => _sleep.Set(false);
    }

    private readonly SleepBlocker _sleep = new();

    private void UpdateSleepBlock()
    {
        var keepAwake = _vm.WorkingCount > 0 && _vm.Settings.KeepAwakeWhileWorking;
        if (_sleep.Set(keepAwake)) MainViewModel.StatusLog($"keep awake: {keepAwake}");
    }

    /// <summary>Records what happened, if Kovan is in the background, and shows everything still pending.</summary>
    private void Notify(Func<AgentNotifications, Notice?> record)
    {
        if (IsActive) return;
        // Read every time: settings.json may have been edited by hand
        var settings = _vm.Settings;
        _notices.NotifyWhenWaiting = settings.NotifyWhenWaiting;
        _notices.NotifyWhenDone = settings.NotifyWhenDone;
        if (record(_notices) == null) return;
        // What changed meanwhile (closed nodes, prompts answered in the terminal), then the summary again
        _notices.Prune(_vm.Nodes);
        if (_notices.Describe() is not { } current) return;
        if (_notifier == null)
        {
            _notifier = new TrayNotifier();
            _notifier.Clicked += OnNoticeClicked;
        }
        _noticeTarget = current.Target;
        var shown = _notifier.Show(current.Title, current.Text, warning: current.NeedsYou);
        MainViewModel.StatusLog($"notification \"{current.Title}\" / \"{current.Text.Replace('\n', ' ')}\" shown={shown}");
    }

    private void OnNoticeClicked()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (_noticeTarget is { } node)
        {
            _vm.Show(node);
            _currentNode = node;
        }
        MainViewModel.StatusLog($"notification clicked -> {_noticeTarget?.Title}");
    }
}
