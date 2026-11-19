using CommunityToolkit.Mvvm.Input;

namespace KovanTerm.ViewModels;

/// <summary>Sending one prompt to several agents (#78).</summary>
public partial class MainViewModel
{
    /// <summary>How long to wait between the paste and Enter: some agents take a paste followed at once by Enter as part of it.</summary>
    public static readonly TimeSpan EnterAfterPaste = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// The terminals the dialog offers, every workspace's, this one's first. Checked: <paramref name="selected"/> if any
    /// are terminals, otherwise this workspace's agents.
    /// </summary>
    public BroadcastViewModel CreateBroadcast(IReadOnlyCollection<TerminalNodeViewModel>? selected = null)
    {
        var terminals = Nodes.Where(n => n.IsTerminal && !n.HasStartError && !n.IsStarting)
            .OrderBy(n => n.IsInActiveWorkspace ? 0 : 1)
            .ThenBy(n => string.IsNullOrEmpty(n.AgentKind) ? 1 : 0)
            .ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var chosen = selected?.Where(terminals.Contains).ToHashSet() ?? [];
        bool Checked(TerminalNodeViewModel n) => chosen.Count > 0 ? chosen.Contains(n) : n.IsInActiveWorkspace && !string.IsNullOrEmpty(n.AgentKind);
        return new BroadcastViewModel(terminals.Select(n => new BroadcastTarget(n, Checked(n))));
    }

    /// <summary>Opens "Send to agents" and sends what the user wrote; the number of terminals it went to.</summary>
    public async Task<int> BroadcastAsync(IReadOnlyCollection<TerminalNodeViewModel>? selected = null)
    {
        var dialog = CreateBroadcast(selected);
        if (dialog.Targets.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Send to agents", "There's no terminal to send to.");
            return 0;
        }
        if (!_dialogs.ShowBroadcast(dialog)) return 0;
        return await SendToAsync(dialog.Checked, dialog.Text, dialog.PressEnter);
    }

    [RelayCommand]
    private Task Broadcast() => BroadcastAsync();

    /// <summary>Pastes <paramref name="text"/> into each terminal as one paste, then presses Enter if asked.</summary>
    public async Task<int> SendToAsync(IReadOnlyList<TerminalNodeViewModel> targets, string text, bool pressEnter)
    {
        text = text.Trim();
        var live = targets.Where(Nodes.Contains).ToList();
        if (text.Length == 0 || live.Count == 0) return 0;
        foreach (var node in live)
            TerminalInputRequested?.Invoke(node, ChangesViewModel.BracketedPaste(text));
        if (pressEnter)
        {
            await Task.Delay(EnterAfterPaste);
            foreach (var node in live.Where(Nodes.Contains))
                TerminalInputRequested?.Invoke(node, "\r");
        }
        StatusLog($"sent to {string.Join(", ", live.Select(n => n.Title))} (enter={pressEnter}): {text.Length} chars");
        return live.Count;
    }
}
