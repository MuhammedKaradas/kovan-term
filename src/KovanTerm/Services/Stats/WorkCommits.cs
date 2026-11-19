using System.Globalization;
using System.IO;

namespace KovanTerm.Services.Stats;

/// <summary>A commit in a project's repository: what got made, next to what was asked.</summary>
public sealed record WorkCommit(string Hash, DateTime Date, string Subject, string Repository);

/// <summary>
/// The commits made in the projects the conversations ran in: by the repository's configured git user, over the last
/// year, all branches. A rebased or cherry-picked commit is counted once.
/// </summary>
public static class WorkCommits
{
    /// <summary>Repository roots of the folders (one per repository, folders that aren't in one skipped).</summary>
    public static IReadOnlyList<string> Repositories(IEnumerable<string> folders) =>
        folders.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !GitRunner.TryParseWslFolder(f, out _, out _) && Directory.Exists(f))
            .Select(f => GitWorktrees.FindRepoRoot(f))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static async Task<List<WorkCommit>> LoadAsync(IEnumerable<string> repositories, DateTime since)
    {
        var all = new List<WorkCommit>();
        foreach (var repo in repositories)
        {
            var email = await GitRunner.RunAsync(repo, "config", "user.email");
            var args = new List<string>
            {
                "log", "--all", "--no-merges", $"--since=@{new DateTimeOffset(since).ToUnixTimeSeconds()}",
                "--pretty=format:%H%x1f%at%x1f%s",
            };
            if (email.Ok && email.Output.Trim() is { Length: > 0 } me) args.Add($"--author={me}");
            var log = await GitRunner.RunAsync(repo, [.. args], [0], TimeSpan.FromSeconds(20));
            if (log.Ok) all.AddRange(Parse(log.Output, repo));
        }
        // A repository reached through two checkouts (a worktree and its main one) lists the same commits
        return all.DistinctBy(c => c.Hash).ToList();
    }

    /// <summary>Lines of <c>hash␟unix time␟subject</c>; the same time and subject seen twice is one commit.</summary>
    public static IEnumerable<WorkCommit> Parse(string output, string repository)
    {
        var seen = new HashSet<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\x1f', 3);
            if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) continue;
            if (!seen.Add(parts[1] + " " + parts[2])) continue;
            yield return new WorkCommit(parts[0], DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime, parts[2], repository);
        }
    }
}
