using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.Services.Mesh;
using KovanTerm.Services.Stats;

namespace KovanTerm.ViewModels;

/// <summary>A worktree's pull request on Home.</summary>
public sealed record PullRequestRow(TerminalNodeViewModel Node, string Branch, string Summary, bool NeedsAttention);

/// <summary>Time, tokens and sessions over a range of days.</summary>
public sealed record WorkSummary(TimeSpan Time, long Tokens, int Sessions)
{
    public static WorkSummary Zero { get; } = new(TimeSpan.Zero, 0, 0);

    public string TimeText => Time.TotalMinutes < 1 ? "0 m" : Time.TotalHours >= 1 ? $"{(int)Time.TotalHours} h {Time.Minutes} m" : $"{Time.Minutes} m";
    public string TokensText => TranscriptUsage.Format(Tokens);
    public string SessionsText => Sessions == 1 ? "1 session" : $"{Sessions} sessions";
}

/// <summary>
/// Home: what needs you and what's at work on every workspace, today's and this week's work, the plan limits, the
/// worktrees' pull requests and the Board's open tasks, on one screen (#139 follow-up, after omux's Home). Kept up to
/// date while it's open; clicking an agent goes to it.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Func<IReadOnlyList<WorkSession>> _loadStats;
    private readonly Func<DateTime> _now;

    public HomeViewModel(MainViewModel main, Func<IReadOnlyList<WorkSession>> loadStats, Func<DateTime>? now = null)
    {
        _main = main;
        _loadStats = loadStats;
        _now = now ?? (() => DateTime.Now);
        Refresh();
    }

    /// <summary>For the plan limits, which the Agents panel keeps up to date.</summary>
    public MainViewModel Main => _main;

    public ObservableCollection<TerminalNodeViewModel> NeedsYou { get; } = [];
    public ObservableCollection<OutsideSessionViewModel> Elsewhere { get; } = [];
    public ObservableCollection<TerminalNodeViewModel> AtWork { get; } = [];
    public ObservableCollection<PullRequestRow> PullRequests { get; } = [];
    public ObservableCollection<MeshTask> Tasks { get; } = [];

    public bool NobodyWaits => NeedsYou.Count == 0 && Elsewhere.Count == 0;
    public bool NothingAtWork => AtWork.Count == 0;
    public bool NoPullRequests => PullRequests.Count == 0;
    public bool NoTasks => Tasks.Count == 0;

    [ObservableProperty] public partial WorkSummary Today { get; private set; } = WorkSummary.Zero;
    [ObservableProperty] public partial WorkSummary Week { get; private set; } = WorkSummary.Zero;
    [ObservableProperty] public partial bool IsLoadingStats { get; private set; }

    /// <summary>Rebuilds the lists from the nodes; cheap, and a list is only touched when what it shows changed.</summary>
    public void Refresh()
    {
        var agents = _main.Nodes.Where(n => n.IsTerminal).ToList();
        Sync(NeedsYou, agents.Where(n => n.Status == NodeStatus.Waiting).ToList());
        Sync(AtWork, agents.Where(n => n.Status == NodeStatus.Working).ToList());
        Sync(Elsewhere, _main.OutsideSessions.Where(s => s.Status == NodeStatus.Waiting).ToList());
        var prs = agents.Where(n => n is { Worktree: not null, PullRequest: not null })
            .Select(n => new PullRequestRow(n, n.Worktree!.Branch, n.PullRequest!.Summary + (n.HasConflicts ? " · conflicts" : ""),
                n.HasFailedChecks || n.HasConflicts))
            .OrderByDescending(r => r.NeedsAttention).ThenBy(r => r.Branch, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!prs.SequenceEqual(PullRequests)) Replace(PullRequests, prs);
        var tasks = _main.MeshBoard.Tasks.Where(t => t.State != "done").OrderBy(t => t.State == "doing" ? 0 : 1).ThenBy(t => t.Id).Take(8).ToList();
        if (!tasks.SequenceEqual(Tasks)) Replace(Tasks, tasks);
        OnPropertyChanged(nameof(NobodyWaits));
        OnPropertyChanged(nameof(NothingAtWork));
        OnPropertyChanged(nameof(NoPullRequests));
        OnPropertyChanged(nameof(NoTasks));
    }

    /// <summary>Reads today's and this week's work from the agents' session files (cached), in the background.</summary>
    [RelayCommand]
    public async Task LoadStatsAsync()
    {
        IsLoadingStats = true;
        var now = _now();
        var sessions = await Task.Run(_loadStats);
        (Today, Week) = (Summarize(sessions, DateOnly.FromDateTime(now), now), Summarize(sessions, WeekStart(now), now));
        IsLoadingStats = false;
    }

    /// <summary>Monday of this week.</summary>
    public static DateOnly WeekStart(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
    }

    /// <summary>Active time (spans cut to the range), tokens and sessions active from <paramref name="from"/> to now.</summary>
    public static WorkSummary Summarize(IEnumerable<WorkSession> sessions, DateOnly from, DateTime now)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var today = DateOnly.FromDateTime(now);
        long ticks = 0, tokens = 0;
        var count = 0;
        foreach (var s in sessions)
        {
            var active = 0L;
            foreach (var span in s.Spans)
            {
                var a = span.Start < start ? start : span.Start;
                var b = span.End > now ? now : span.End;
                if (b > a) active += (b - a).Ticks;
            }
            // TokensIn leaves its end out: up to tomorrow takes today in
            var t = s.TokensIn(from, today.AddDays(1)).Total;
            if (active == 0 && t == 0) continue;
            ticks += active;
            tokens += t;
            count++;
        }
        return new WorkSummary(TimeSpan.FromTicks(ticks), tokens, count);
    }

    [RelayCommand]
    private void GoTo(TerminalNodeViewModel? node)
    {
        if (node != null && _main.Nodes.Contains(node)) GoToRequested?.Invoke(node);
    }

    [RelayCommand]
    private void GoToPullRequest(PullRequestRow? row) => GoTo(row?.Node);

    [RelayCommand]
    private void ShowElsewhere() => _main.ShowAgentsPanel = true;

    /// <summary>The window brings Kovan forward on the node.</summary>
    public event Action<TerminalNodeViewModel>? GoToRequested;

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (!items.SequenceEqual(target)) Replace(target, items);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }
}
