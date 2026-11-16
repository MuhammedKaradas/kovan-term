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

        var add = await GitRunner.RunAsync(repoRoot, "worktree", "add", "-b", branch, path, "HEAD");
        return add.Ok
            ? new(new WorktreeInfo(repoRoot, path, branch, head.Output.Trim()))
            : new(null, add.Error);
    }

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
