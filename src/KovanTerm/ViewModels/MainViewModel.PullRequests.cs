using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>
/// Pull requests of worktree nodes (#86): their state and checks on the node, the failed checks' log sent to the agent,
/// and the worktree deleted once the pull request is merged.
/// </summary>
public partial class MainViewModel
{
    private PullRequests? _pullRequests;
    private readonly DispatcherTimer _pullRequestTimer = new() { Interval = TimeSpan.FromMinutes(3) };
    private readonly Dictionary<TerminalNodeViewModel, DateTime> _pullRequestChecked = new();
    /// <summary>A turn's end refreshes at most this often per node (the timer still does every few minutes).</summary>
    private static readonly TimeSpan PullRequestMinInterval = TimeSpan.FromSeconds(30);

    private void InitPullRequests(PullRequests? pullRequests)
    {
        _pullRequests = pullRequests;
        if (pullRequests == null) return;
        _pullRequestTimer.Tick += (_, _) => _ = RefreshPullRequestsAsync();
        _pullRequestTimer.Start();
        _dispatcher.BeginInvoke(() => _ = RefreshPullRequestsAsync(), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Worktree nodes whose pull request can be looked up (gh runs on Windows, so not inside WSL).</summary>
    private bool HasPullRequestSource(TerminalNodeViewModel node) => _pullRequests != null && node.Worktree != null && !node.IsWsl;

    public async Task RefreshPullRequestsAsync()
    {
        foreach (var node in Nodes.Where(HasPullRequestSource).ToList())
            await RefreshPullRequestAsync(node);
    }

    /// <summary>Looks up the node's pull request now, unless that was done moments ago and <paramref name="force"/> is off.</summary>
    public async Task RefreshPullRequestAsync(TerminalNodeViewModel node, bool force = true)
    {
        if (!HasPullRequestSource(node) || node.Worktree is not { } w) return;
        if (!force && _pullRequestChecked.TryGetValue(node, out var last) && DateTime.UtcNow - last < PullRequestMinInterval) return;
        _pullRequestChecked[node] = DateTime.UtcNow;
        var pr = await _pullRequests!.GetAsync(w.Path, w.Branch);
        if (!Nodes.Contains(node)) return;
        if (pr?.Summary != node.PullRequest?.Summary) StatusLog($"{node.Title} {pr?.Summary ?? "no pull request"}");
        node.PullRequest = pr;
    }

    [RelayCommand]
    private void OpenPullRequest(TerminalNodeViewModel? node)
    {
        if (node?.PullRequest is { Url.Length: > 0 } pr) _dialogs.OpenInBrowser(pr.Url);
    }

    /// <summary>Pastes which checks failed and the end of their log into the node's agent, for the user to send.</summary>
    [RelayCommand]
    private async Task SendFailedChecks(TerminalNodeViewModel? node)
    {
        if (node is not { Worktree: { } w, PullRequest: { Checks: ChecksState.Failing } pr } || _pullRequests == null) return;
        node.StatusText = "Reading the failed checks' log…";
        var log = await _pullRequests.FailedLogAsync(w.Path, pr);
        TerminalInputRequested?.Invoke(node, ChangesViewModel.BracketedPaste(PullRequests.FixPrompt(pr, log)));
        StatusLog($"{node.Title} sent failed checks of PR #{pr.Number} ({log.Length} chars of log)");
        node.StatusText = null;
        GoTo(node);
    }

    /// <summary>The pull request is merged: closes the node and deletes its worktree and branch, after saying what would be lost.</summary>
    [RelayCommand]
    private async Task DeleteMergedWorktree(TerminalNodeViewModel? node)
    {
        if (node is not { Worktree: { } w, PullRequest: { State: PullRequestState.Merged } pr }) return;
        var lost = new List<string>();
        var after = await GitRunner.RunAsync(w.Path, "rev-list", "--count", $"{pr.HeadCommit}..HEAD");
        if (after.Ok && int.TryParse(after.Output.Trim(), out var commits) && commits > 0)
            lost.Add($"{Plural(commits, "commit")} made after the pull request");
        if ((await GitWorktrees.StateAsync(w)).Value is { UncommittedFiles: > 0 } state)
            lost.Add(Plural(state.UncommittedFiles, "uncommitted file"));
        var warning = lost.Count > 0 ? $"\n\nThese would be lost: {string.Join(" and ", lost)}." : "";
        if (!await _dialogs.ConfirmAsync("Delete worktree",
                $"Pull request #{pr.Number} was merged. Close this terminal and delete its worktree\n{w.Path}\nand branch {w.Branch}?{warning}",
                "Delete"))
            return;
        await CloseNodeAsync(node, askForNote: false, worktreeChoice: WorktreeCloseChoice.Delete);
    }
}
