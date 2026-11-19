using KovanTerm.Models;

namespace KovanTerm.ViewModels;

/// <summary>What a Windows notification says, and which node clicking it goes to (#80).</summary>
public sealed record Notice(string Title, string Text, TerminalNodeViewModel Target, bool NeedsYou);

/// <summary>
/// Collects what happened while Kovan was in the background (agents that started waiting for the user, turns that
/// finished) and sums it up in one notification: a newer notification replaces the one on screen, so it says
/// everything still pending.
/// </summary>
public sealed class AgentNotifications
{
    private readonly List<TerminalNodeViewModel> _waiting = [];
    private readonly List<TerminalNodeViewModel> _finished = [];

    public bool NotifyWhenWaiting { get; set; } = true;
    public bool NotifyWhenDone { get; set; } = true;

    /// <summary>An agent started waiting for the user; the notification to show, or <c>null</c>.</summary>
    public Notice? Waiting(TerminalNodeViewModel node)
    {
        if (!NotifyWhenWaiting) return null;
        _finished.Remove(node);
        if (!_waiting.Contains(node)) _waiting.Add(node);
        return Describe();
    }

    /// <summary>An agent's turn ended (working → idle or done); the notification to show, or <c>null</c>.</summary>
    public Notice? Finished(TerminalNodeViewModel node)
    {
        _waiting.Remove(node);
        if (!NotifyWhenDone || string.IsNullOrEmpty(node.AgentKind)) return null;
        if (!_finished.Contains(node)) _finished.Add(node);
        return Describe();
    }

    /// <summary>The user is back (Kovan was activated): nothing is pending any more.</summary>
    public void Clear()
    {
        _waiting.Clear();
        _finished.Clear();
    }

    /// <summary>Drops nodes that were closed, and ones no longer waiting (answered in the terminal meanwhile).</summary>
    public void Prune(IReadOnlyCollection<TerminalNodeViewModel> nodes)
    {
        _waiting.RemoveAll(n => !nodes.Contains(n) || n.Status != NodeStatus.Waiting);
        _finished.RemoveAll(n => !nodes.Contains(n));
    }

    public Notice? Describe()
    {
        if (_waiting.Count > 0)
        {
            var first = _waiting[0];
            var title = _waiting.Count == 1 ? $"{first.Title} needs you" : $"{_waiting.Count} agents need you";
            var text = _waiting.Count == 1
                ? Where(first, first.StatusText ?? "It's waiting for your answer.")
                : string.Join(", ", _waiting.Select(n => n.Title));
            if (_finished.Count > 0) text += $"\nAlso finished: {string.Join(", ", _finished.Select(n => n.Title))}";
            return new Notice(title, text, first, NeedsYou: true);
        }
        if (_finished.Count > 0)
        {
            var first = _finished[0];
            return _finished.Count == 1
                ? new Notice($"{first.Title} finished", Where(first, "Its turn is over."), first, NeedsYou: false)
                : new Notice($"{_finished.Count} agents finished", string.Join(", ", _finished.Select(n => n.Title)), first, NeedsYou: false);
        }
        return null;
    }

    private static string Where(TerminalNodeViewModel node, string text) =>
        node.OtherWorkspaceName is { } workspace ? $"{text}\n(in {workspace})" : text;
}
