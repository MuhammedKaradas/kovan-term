using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>How much work a worktree holds that isn't on the main checkout yet.</summary>
/// <param name="UncommittedFiles">Changed or untracked files.</param>
/// <param name="CommitsAhead">Commits on the branch since it started.</param>
public sealed record WorktreeState(int UncommittedFiles, int CommitsAhead)
{
    public bool HasWork => UncommittedFiles > 0 || CommitsAhead > 0;
}

/// <summary>
/// Creates and removes the git worktrees agents work in. A worktree lives next to the repository, in
/// <c>&lt;repo&gt;.worktrees\&lt;branch&gt;</c>, so it's outside the repository's own tree (no nested checkouts)
/// and easy to find.
/// </summary>
public static partial class GitWorktrees
{
    /// <summary>The repository root containing <paramref name="directory"/>, found by looking for <c>.git</c>; cheap, no git process.</summary>
    public static string? FindRepoRoot(string directory)
    {
        try
        {
            for (var dir = new DirectoryInfo(directory); dir != null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git)) return dir.FullName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }

    /// <summary>A branch name for a new agent, e.g. <c>kovan/claude-3f2a</c>.</summary>
    public static string SuggestBranch(string profileId) =>
        $"kovan/{Slug(profileId)}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()}";

    [GeneratedRegex(@"^(?!/)(?!.*//)(?!.*\.\.)(?!.*@\{)(?!.*/$)(?!.*\.lock$)(?!.*\.$)[A-Za-z0-9._/-]+$")]
    private static partial Regex BranchPattern();

    /// <summary>A subset of what git accepts: letters, digits, <c>. _ - /</c>, without the forms git rejects.</summary>
    public static bool IsValidBranch(string name) => name.Length is > 0 and <= 200 && BranchPattern().IsMatch(name);

    /// <summary><c>C:\src\shop</c> + <c>kovan/claude-3f2a</c> → <c>C:\src\shop.worktrees\kovan-claude-3f2a</c>.</summary>
    public static string PathFor(string repoRoot, string branch)
    {
        var root = repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(root + ".worktrees", Slug(branch));
    }

    private static string Slug(string s) => Regex.Replace(s.Replace('/', '-'), "[^A-Za-z0-9._-]", "-").Trim('-', '.');

    /// <summary>Creates a new branch from the current <c>HEAD</c> and checks it out in a new worktree.</summary>
    public static async Task<GitResult<WorktreeInfo>> CreateAsync(string repoRoot, string branch)
    {
        if (!IsValidBranch(branch)) return new(null, $"'{branch}' isn't a valid branch name.");
        var head = await GitRunner.RunAsync(repoRoot, "rev-parse", "--verify", "HEAD");
        if (!head.Ok) return new(null, "The repository has no commits yet; make a first commit, then try again.");
        var path = PathFor(repoRoot, branch);
        if (Directory.Exists(path)) return new(null, $"The folder already exists:\n{path}");

        var baseBranch = await CurrentBranchAsync(repoRoot);
        var add = await GitRunner.RunAsync(repoRoot, "worktree", "add", "-b", branch, path, "HEAD");
        return add.Ok
            ? new(new WorktreeInfo(repoRoot, path, branch, head.Output.Trim(), baseBranch))
            : new(null, add.Error);
    }

    /// <summary>The branch checked out in <paramref name="directory"/>; <c>null</c> for a detached HEAD.</summary>
    public static async Task<string?> CurrentBranchAsync(string directory)
    {
        var r = await GitRunner.RunAsync(directory, "symbolic-ref", "--short", "-q", "HEAD");
        return r.Ok && r.Output.Trim() is { Length: > 0 } name ? name : null;
    }

    /// <summary>
    /// Merges the worktree's branch into its base branch, in the main checkout. Refuses (without touching anything)
    /// when the main checkout is on another branch or has uncommitted changes to tracked files; on conflicts the
    /// merge is aborted, so the main checkout is never left half-merged. Returns a summary for the user.
    /// </summary>
    public static async Task<GitResult<string>> MergeAsync(WorktreeInfo w)
    {
        if (w.BaseBranch is not { } target)
            return new(null, "This worktree doesn't know which branch it started from; merge it by hand.");
        if (await CurrentBranchAsync(w.RepoRoot) != target)
            return new(null, $"The main checkout isn't on {target} any more. Check out {target} there, then try again.");
        var dirty = await GitRunner.RunAsync(w.RepoRoot, "status", "--porcelain=v1", "--untracked-files=no");
        if (!dirty.Ok) return new(null, dirty.Error);
        if (dirty.Output.Trim().Length > 0)
            return new(null, $"The main checkout has uncommitted changes. Commit or stash them, then try again.\n{w.RepoRoot}");

        var ahead = await GitRunner.RunAsync(w.RepoRoot, "rev-list", "--count", $"{target}..{w.Branch}");
        if (ahead.Ok && ahead.Output.Trim() == "0") return new(null, $"{w.Branch} has no commits that aren't on {target} yet.");

        var merge = await GitRunner.RunAsync(w.RepoRoot, "merge", "--no-edit", w.Branch);
        if (merge.Ok) return new($"Merged {w.Branch} into {target}.");

        var conflicts = await GitRunner.RunAsync(w.RepoRoot, "diff", "--name-only", "--diff-filter=U");
        await GitRunner.RunAsync(w.RepoRoot, "merge", "--abort");
        var files = conflicts.Ok ? conflicts.Output.Trim() : "";
        return new(null, files.Length > 0
            ? $"The merge has conflicts, so it was cancelled; {target} is unchanged. Conflicting files:\n{files}\n\nAsk the agent to merge {target} into its branch first, then try again."
            : $"The merge failed and was cancelled: {merge.Error}");
    }

    /// <summary>Pushes the worktree's branch to <c>origin</c> and returns the page to open a pull request, if the host is known.</summary>
    public static async Task<GitResult<string?>> PushAsync(WorktreeInfo w)
    {
        var remote = await GitRunner.RunAsync(w.Path, "remote", "get-url", "origin");
        if (!remote.Ok) return new(null, "The repository has no 'origin' remote to push to.");
        var push = await GitRunner.RunAsync(w.Path, ["push", "-u", "origin", w.Branch], [0], TimeSpan.FromMinutes(2));
        if (!push.Ok) return new(null, push.Error);
        return new(PullRequestUrl(remote.Output.Trim(), w.BaseBranch, w.Branch));
    }

    /// <summary>
    /// The "new pull request" page for a GitHub or GitLab remote (HTTPS or SSH form); <c>null</c> for other hosts.
    /// </summary>
    public static string? PullRequestUrl(string remoteUrl, string? baseBranch, string branch)
    {
        var m = Regex.Match(remoteUrl.Trim(), @"^(?:https?://(?:[^@/]+@)?|ssh://git@|git@)(?<host>github\.com|gitlab\.com)[:/](?<path>.+?)(?:\.git)?/?$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var repo = $"https://{m.Groups["host"].Value.ToLowerInvariant()}/{m.Groups["path"].Value}";
        var head = Uri.EscapeDataString(branch).Replace("%2F", "/");
        if (m.Groups["host"].Value.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return baseBranch is { } b ? $"{repo}/compare/{Uri.EscapeDataString(b).Replace("%2F", "/")}...{head}?expand=1" : $"{repo}/pull/new/{head}";
        var url = $"{repo}/-/merge_requests/new?merge_request%5Bsource_branch%5D={Uri.EscapeDataString(branch)}";
        return baseBranch is { } t ? $"{url}&merge_request%5Btarget_branch%5D={Uri.EscapeDataString(t)}" : url;
    }

    /// <summary>
    /// Wraps the agent's command line so the setup command runs first, in the worktree, and the agent starts only if
    /// it succeeds. <c>KOVAN_REPO</c> points at the main checkout (e.g. <c>copy "%KOVAN_REPO%\.env" .</c>).
    /// <c>cmd /k</c> keeps the terminal open when something fails, so the user sees why.
    /// cmd expands <c>%…%</c> for the whole line before <c>set</c> runs, so <c>%KOVAN_REPO%</c> in the command is
    /// replaced here; the variable is still set for scripts the command starts.
    /// </summary>
    public static string WithSetup(string commandLine, string setup, string repoRoot) =>
        $"cmd.exe /s /k \"set \"KOVAN_REPO={repoRoot}\" && {setup.Replace("%KOVAN_REPO%", repoRoot, StringComparison.OrdinalIgnoreCase)} && {commandLine}\"";

    /// <summary>Uncommitted files and commits since the branch started.</summary>
    public static async Task<GitResult<WorktreeState>> StateAsync(WorktreeInfo w)
    {
        var status = await GitRunner.RunAsync(w.Path, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        if (!status.Ok) return new(null, status.Error);
        var ahead = await GitRunner.RunAsync(w.Path, "rev-list", "--count", $"{w.BaseCommit}..HEAD");
        return new(new WorktreeState(
            GitChanges.ParseStatus(status.Output).Count,
            ahead.Ok && int.TryParse(ahead.Output.Trim(), out var n) ? n : 0));
    }

    /// <summary>
    /// Removes the worktree's folder and deletes its branch, uncommitted changes and commits included. The caller
    /// asks the user first. Retries for a moment: the node's shell may still be releasing the folder.
    /// </summary>
    public static async Task<GitResult<bool>> RemoveAsync(WorktreeInfo w)
    {
        GitOutput remove = default;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            remove = await GitRunner.RunAsync(w.RepoRoot, "worktree", "remove", "--force", w.Path);
            if (remove.Ok || !Directory.Exists(w.Path)) break;
            await Task.Delay(400);
        }
        if (!remove.Ok && Directory.Exists(w.Path)) return new(false, remove.Error);
        await GitRunner.RunAsync(w.RepoRoot, "worktree", "prune");
        var branch = await GitRunner.RunAsync(w.RepoRoot, "branch", "-D", w.Branch);
        // Leave the <repo>.worktrees folder alone unless it's now empty
        TryDeleteEmpty(Path.GetDirectoryName(w.Path));
        return branch.Ok ? new(true) : new(false, $"The worktree was removed, but the branch wasn't: {branch.Error}");
    }

    private static void TryDeleteEmpty(string? dir)
    {
        try
        {
            if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
