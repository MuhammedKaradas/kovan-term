namespace KovanTerm.Services;

/// <summary>A changed file in a working tree.</summary>
/// <param name="Path">Repo-relative path with forward slashes.</param>
/// <param name="Code">Short status: <c>M</c>, <c>A</c>, <c>D</c>, <c>R</c>, <c>?</c> (untracked)…</param>
/// <param name="OldPath">For renames: the previous path.</param>
public sealed record ChangedFile(string Path, string Code, string? OldPath = null)
{
    public bool IsUntracked => Code == "?";
    public string FileName => System.IO.Path.GetFileName(Path);
    public string? Folder => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') is { Length: > 0 } f ? f : null;
}

/// <summary>Result of a git query: the value, or why there's none (not a repository, git missing…).</summary>
public sealed record GitResult<T>(T? Value, string? Error = null);

/// <summary>
/// Reads a working tree's changes with the <c>git</c> command line. Read-only: never changes the repository.
/// Without a base, changes are relative to <c>HEAD</c> (what isn't committed yet). With a base commit (a worktree
/// node: the commit its branch started from), committed work on the branch is included too.
/// </summary>
public sealed class GitChanges
{
    /// <summary>Changed and untracked files of the repository that contains <paramref name="directory"/>.</summary>
    public async Task<GitResult<IReadOnlyList<ChangedFile>>> StatusAsync(string directory, string? baseCommit = null)
    {
        if (baseCommit == null)
        {
            var status = await GitRunner.RunAsync(directory, "status", "--porcelain=v1", "-z", "--untracked-files=all");
            return status.Ok ? new(ParseStatus(status.Output)) : new(null, status.Error);
        }
        var root = await RootAsync(directory);
        if (root.Value == null) return new(null, root.Error);
        var changed = await GitRunner.RunAsync(root.Value, "diff", "--name-status", "-z", "-M", baseCommit);
        if (!changed.Ok) return new(null, changed.Error);
        var untracked = await GitRunner.RunAsync(root.Value, "ls-files", "--others", "--exclude-standard", "-z");
        if (!untracked.Ok) return new(null, untracked.Error);
        return new(ParseNameStatus(changed.Output)
            .Concat(untracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(p => new ChangedFile(p, "?")))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>The file's diff against <c>HEAD</c> (or the base commit), or its whole content if it's untracked.</summary>
    public async Task<GitResult<string>> DiffAsync(string directory, ChangedFile file, string? baseCommit = null)
    {
        var root = await RootAsync(directory);
        if (root.Value == null) return new(null, root.Error);
        // Untracked: diff against an empty file; exit code 1 means "differences found", not an error
        var result = file.IsUntracked
            ? await GitRunner.RunAsync(root.Value, ["diff", "--no-color", "--no-ext-diff", "--no-index", "--", "NUL", file.Path], [0, 1])
            : await GitRunner.RunAsync(root.Value,
                ["diff", "--no-color", "--no-ext-diff", "-M", baseCommit ?? (await HasHeadAsync(root.Value) ? "HEAD" : "--cached"), "--",
                    .. file.OldPath is { } old ? new[] { old, file.Path } : [file.Path]], [0]);
        return result.Ok ? new(result.Output) : new(null, result.Error);
    }

    private static async Task<GitResult<string>> RootAsync(string directory)
    {
        var r = await GitRunner.RunAsync(directory, "rev-parse", "--show-toplevel");
        return r.Ok ? new(r.Output.Trim()) : new(null, r.Error);
    }

    private static async Task<bool> HasHeadAsync(string root) => (await GitRunner.RunAsync(root, "rev-parse", "--verify", "--quiet", "HEAD")).Ok;

    /// <summary>Parses <c>git status --porcelain=v1 -z</c>.</summary>
    public static IReadOnlyList<ChangedFile> ParseStatus(string output)
    {
        var result = new List<ChangedFile>();
        var parts = output.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var entry = parts[i];
            if (entry.Length < 4) continue;
            var x = entry[0];
            var y = entry[1];
            var path = entry[3..];
            string? oldPath = null;
            // Renames and copies are followed by the original path
            if (x is 'R' or 'C') oldPath = i + 1 < parts.Length ? parts[++i] : null;
            var code = (x, y) switch
            {
                ('?', '?') => "?",
                ('R', _) => "R",
                ('C', _) => "C",
                (_, 'D') or ('D', _) => "D",
                ('A', _) => "A",
                _ => "M",
            };
            result.Add(new ChangedFile(path, code, oldPath));
        }
        return result.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Parses <c>git diff --name-status -z</c>: <c>M\0path\0</c>, renames <c>R100\0old\0new\0</c>.</summary>
    public static IReadOnlyList<ChangedFile> ParseNameStatus(string output)
    {
        var result = new List<ChangedFile>();
        var parts = output.Split('\0');
        for (var i = 0; i + 1 < parts.Length; i++)
        {
            var status = parts[i];
            if (status.Length == 0) continue;
            var code = status[0] switch { 'A' => "A", 'D' => "D", 'R' => "R", 'C' => "C", _ => "M" };
            if (code is "R" or "C")
            {
                if (i + 2 >= parts.Length) break;
                result.Add(new ChangedFile(parts[i + 2], code, parts[i + 1]));
                i += 2;
            }
            else
            {
                result.Add(new ChangedFile(parts[i + 1], code));
                i += 1;
            }
        }
        return result;
    }
}
