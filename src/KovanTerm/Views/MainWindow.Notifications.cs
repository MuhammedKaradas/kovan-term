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
        _vm.AttentionNeeded += NotifyWaitingSoon;
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

        // The status light follows the counts and whether Kovan is in front
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.WorkingCount) or nameof(MainViewModel.WaitingCount)) UpdateStatusLight();
        };
        Activated += (_, _) => UpdateStatusLight();
        Deactivated += (_, _) => UpdateStatusLight();
        StateChanged += (_, _) => UpdateStatusLight();
        Closed += (_, _) => _statusLight?.Close();
    }

    private StatusLightWindow? _statusLight;

    /// <summary>The user is looking at Kovan: active and not minimized (minimized, Windows may leave it active).</summary>
    private bool InFront => IsActive && WindowState != WindowState.Minimized;

    /// <summary>Shown while it's turned on, Kovan isn't the active window and an agent works or waits.</summary>
    private void UpdateStatusLight()
    {
        var settings = _vm.Settings;
        var (working, waiting) = (_vm.WorkingCount, _vm.WaitingCount);
        if (!settings.StatusLight || InFront || working + waiting == 0 || !IsLoaded)
        {
            _statusLight?.Hide();
            return;
        }
        if (_statusLight == null)
        {
            _statusLight = new StatusLightWindow();
            _statusLight.Clicked += OnStatusLightClicked;
            _statusLight.Moved += (right, top) =>
                _vm.SaveSettings(s => s with { StatusLightRightEdge = right, StatusLightTop = top });
            _statusLight.TurnedOff += () =>
            {
                _vm.SaveSettings(s => s with { StatusLight = false });
                _statusLight?.Hide();
            };
        }
        _statusLight.SetPosition(settings.StatusLightRightEdge, settings.StatusLightTop);
        _statusLight.ShowCounts(working, waiting);
    }

    private void OnStatusLightClicked()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (_vm.WaitingCount > 0) _vm.GoToWaitingCommand.Execute(null);
        MainViewModel.StatusLog("status light clicked");
    }

    private readonly SleepBlocker _sleep = new();

    private void UpdateSleepBlock()
    {
        var keepAwake = _vm.WorkingCount > 0 && _vm.Settings.KeepAwakeWhileWorking;
        if (_sleep.Set(keepAwake)) MainViewModel.StatusLog($"keep awake: {keepAwake}");
    }

    /// <summary>
    /// How long an agent waits before it's announced: some report "waiting" for a moment on their own (Antigravity
    /// checks its sign-in on every start), and a prompt answered within this time needs no notification.
    /// </summary>
    private static readonly TimeSpan WaitingGrace = TimeSpan.FromSeconds(3);

    private async void NotifyWaitingSoon(TerminalNodeViewModel node)
    {
        await Task.Delay(WaitingGrace);
        if (node.Status == Models.NodeStatus.Waiting && _vm.Nodes.Contains(node)) Notify(n => n.Waiting(node));
    }

    /// <summary>Records what happened, if Kovan is in the background, and shows everything still pending.</summary>
    private void Notify(Func<AgentNotifications, Notice?> record)
    {
        if (InFront) return;
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
