using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

public enum PullRequestState { Open, Draft, Merged, Closed }

public enum ChecksState { None, Pending, Passing, Failing }

/// <param name="RunId">The GitHub Actions run, if the check is one (its log can be fetched).</param>
public sealed record FailedCheck(string Name, string? Url, long? RunId);

/// <param name="HeadCommit">The commit the pull request ends at (its branch on the remote).</param>
public sealed record PullRequestStatus(int Number, string Url, PullRequestState State, ChecksState Checks, string HeadCommit,
    IReadOnlyList<FailedCheck> Failed)
{
    /// <summary>"PR #12 · checks failing", for the node's header and the Changes panel.</summary>
    public string Summary => $"PR #{Number} · " + (State switch
    {
        PullRequestState.Merged => "merged",
        PullRequestState.Closed => "closed",
        _ => (State == PullRequestState.Draft ? "draft, " : "") + Checks switch
        {
            ChecksState.Failing => "checks failing",
            ChecksState.Pending => "checks running",
            ChecksState.Passing => "checks passed",
            _ => "open",
        },
    });
}

/// <summary>
/// A worktree branch's pull request on GitHub, through the GitHub CLI (<c>gh</c>), which keeps the user's login:
/// its state, its checks, and the log of the ones that failed.
/// </summary>
public sealed partial class PullRequests(string ghPath)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The pull request for <paramref name="branch"/>; <c>null</c> if there's none (or gh can't tell).</summary>
    public async Task<PullRequestStatus?> GetAsync(string repoDirectory, string branch)
    {
        var (ok, output) = await RunAsync(repoDirectory, "pr", "view", branch, "--json", "number,url,state,isDraft,headRefOid,statusCheckRollup");
        return ok ? Parse(output) : null;
    }

    /// <summary>The end of the failed steps' log of each failed Actions run, cleaned up to paste into an agent.</summary>
    public async Task<string> FailedLogAsync(string repoDirectory, PullRequestStatus pr)
    {
        var text = new StringBuilder();
        foreach (var runId in pr.Failed.Select(f => f.RunId).OfType<long>().Distinct().Take(3))
        {
            var (ok, output) = await RunAsync(repoDirectory, "run", "view", runId.ToString(), "--log-failed");
            if (ok && output.Length > 0) text.Append(LastLines(Clean(output), MaxLogLines)).Append('\n');
        }
        var log = text.ToString();
        return log.Length > MaxLogChars ? "…" + log[^MaxLogChars..] : log;
    }

    public const int MaxLogLines = 120, MaxLogChars = 10_000;

    /// <summary>What's typed into the agent: which checks failed and how, for it to fix them.</summary>
    public static string FixPrompt(PullRequestStatus pr, string log)
    {
        var s = new StringBuilder($"The checks of pull request #{pr.Number} failed:\n");
        foreach (var f in pr.Failed) s.Append($"- {f.Name}{(f.Url != null ? $" ({f.Url})" : "")}\n");
        if (log.Trim().Length > 0) s.Append("\nThe end of the failed steps' log:\n```\n").Append(log.TrimEnd()).Append("\n```\n");
        s.Append("\nFind out why and fix it.");
        return s.ToString();
    }

    public static PullRequestStatus? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var state = Str(r, "state") switch
            {
                "MERGED" => PullRequestState.Merged,
                "CLOSED" => PullRequestState.Closed,
                _ => r.TryGetProperty("isDraft", out var d) && d.ValueKind == JsonValueKind.True ? PullRequestState.Draft : PullRequestState.Open,
            };
            var failed = new List<FailedCheck>();
            bool pending = false, any = false;
            if (r.TryGetProperty("statusCheckRollup", out var checks) && checks.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in checks.EnumerateArray())
                {
                    any = true;
                    // A check run (Actions, apps) or a commit status (older integrations)
                    var isRun = Str(c, "__typename") == "CheckRun";
                    var name = Str(c, isRun ? "name" : "context") ?? "check";
                    var url = Str(c, isRun ? "detailsUrl" : "targetUrl");
                    var outcome = isRun
                        ? Str(c, "status") == "COMPLETED" ? Str(c, "conclusion") : "PENDING"
                        : Str(c, "state");
                    switch (outcome)
                    {
                        case "FAILURE" or "ERROR" or "TIMED_OUT" or "CANCELLED" or "STARTUP_FAILURE" or "ACTION_REQUIRED":
                            failed.Add(new FailedCheck(name, url, RunIdOf(url)));
                            break;
                        case "PENDING" or "EXPECTED" or "" or null:
                            pending = true;
                            break;
                    }
                }
            }
            var checksState = failed.Count > 0 ? ChecksState.Failing : pending ? ChecksState.Pending : any ? ChecksState.Passing : ChecksState.None;
            return new PullRequestStatus(r.GetProperty("number").GetInt32(), Str(r, "url") ?? "", state, checksState,
                Str(r, "headRefOid") ?? "", failed);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>An Actions run's id from a check's link (<c>…/actions/runs/123/job/456</c>).</summary>
    public static long? RunIdOf(string? url) =>
        url != null && RunUrl().Match(url) is { Success: true } m && long.TryParse(m.Groups[1].Value, out var id) ? id : null;

    /// <summary>Without colors, carriage returns, a byte order mark or other control characters (they'd reach the agent).</summary>
    public static string Clean(string log)
    {
        log = Ansi().Replace(log, "");
        var s = new StringBuilder(log.Length);
        foreach (var c in log)
            if (c is '\n' or '\t' || (!char.IsControl(c) && c != '﻿')) s.Append(c);
        return s.ToString();
    }

    private static string LastLines(string text, int count)
    {
        var lines = text.TrimEnd().Split('\n');
        return string.Join('\n', lines.Length > count ? lines[^count..] : lines);
    }

    private static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private async Task<(bool Ok, string Output)> RunAsync(string directory, params string[] args)
    {
        var info = new ProcessStartInfo(ghPath)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        // No prompts, no pager, no colors
        info.Environment["GH_PROMPT_DISABLED"] = "1";
        info.Environment["GH_PAGER"] = "";
        info.Environment["NO_COLOR"] = "1";
        Process? process = null;
        try
        {
            process = Process.Start(info)!;
            using var timeout = new CancellationTokenSource(Timeout);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode == 0, await output);
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Debug.WriteLine($"gh {string.Join(' ', args)} failed: {ex.Message}");
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
            catch (Exception kill) when (kill is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return (false, "");
        }
        finally
        {
            process?.Dispose();
        }
    }

    [GeneratedRegex(@"/actions/runs/(\d+)")] private static partial Regex RunUrl();
    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")] private static partial Regex Ansi();
}
