using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

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

/// <summary>Reads a working tree's changes with the <c>git</c> command line. Read-only: never changes the repository.</summary>
public sealed class GitChanges
{
    /// <summary>Larger diffs are cut; a terminal agent can produce huge generated files.</summary>
    public const int MaxDiffBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Changed and untracked files of the repository that contains <paramref name="directory"/>.</summary>
    public async Task<GitResult<IReadOnlyList<ChangedFile>>> StatusAsync(string directory)
    {
        var (ok, output, error) = await RunAsync(directory, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        return ok ? new(ParseStatus(output)) : new(null, error);
    }

    /// <summary>The file's diff against <c>HEAD</c> (staged and unstaged), or its whole content if it's untracked.</summary>
    public async Task<GitResult<string>> DiffAsync(string directory, ChangedFile file)
    {
        var root = await RootAsync(directory);
        if (root.Value == null) return new(null, root.Error);
        // Untracked: diff against an empty file; exit code 1 means "differences found", not an error
        var (ok, output, error) = file.IsUntracked
            ? await RunAsync(root.Value, ["diff", "--no-color", "--no-ext-diff", "--no-index", "--", "NUL", file.Path], okExitCodes: [0, 1])
            : await RunAsync(root.Value, ["diff", "--no-color", "--no-ext-diff", await HasHeadAsync(root.Value) ? "HEAD" : "--cached", "--", file.Path]);
        return ok ? new(output) : new(null, error);
    }

    private async Task<GitResult<string>> RootAsync(string directory)
    {
        var (ok, output, error) = await RunAsync(directory, "rev-parse", "--show-toplevel");
        return ok ? new(output.Trim()) : new(null, error);
    }

    private async Task<bool> HasHeadAsync(string root) => (await RunAsync(root, "rev-parse", "--verify", "--quiet", "HEAD")).Ok;

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

    private Task<(bool Ok, string Output, string? Error)> RunAsync(string directory, params string[] args) =>
        RunAsync(directory, args, okExitCodes: [0]);

    private static async Task<(bool Ok, string Output, string? Error)> RunAsync(string directory, string[] args, int[] okExitCodes)
    {
        if (!Directory.Exists(directory)) return (false, "", "The folder doesn't exist.");
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Read-only and quiet: no index refresh lock fighting the agent's own git commands, no pager, paths unquoted
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var a in new[] { "-c", "core.quotepath=false", "-c", "core.fsmonitor=false" }.Concat(args))
            psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Win32Exception)
        {
            return (false, "", "git wasn't found. Install Git for Windows to see changes.");
        }
        using (process)
        {
            using var cts = new CancellationTokenSource(Timeout);
            var stdout = ReadCapped(process.StandardOutput, MaxDiffBytes);
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (false, "", "git didn't answer in time.");
            }
            var output = await stdout;
            if (okExitCodes.Contains(process.ExitCode)) return (true, output, null);
            var message = (await stderr).Trim();
            return (false, "", message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                ? "Not a git repository."
                : message.Length > 0 ? message.Split('\n')[0] : $"git exited with code {process.ExitCode}.");
        }
    }

    /// <summary>Reads at most <paramref name="max"/> chars; the rest is drained so git doesn't block on a full pipe.</summary>
    private static async Task<string> ReadCapped(StreamReader reader, int max)
    {
        var sb = new StringBuilder();
        var buffer = new char[64 * 1024];
        int n;
        var truncated = false;
        while ((n = await reader.ReadAsync(buffer)) > 0)
        {
            if (sb.Length < max) sb.Append(buffer, 0, Math.Min(n, max - sb.Length));
            else truncated = true;
        }
        if (truncated) sb.Append("\n\\ Diff cut: larger than 2 MB.\n");
        return sb.ToString();
    }
}
