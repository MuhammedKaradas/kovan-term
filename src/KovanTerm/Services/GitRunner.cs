using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KovanTerm.Services;

/// <summary>Result of running git: its output, or why it failed in words a user can read.</summary>
public readonly record struct GitOutput(bool Ok, string Output, string? Error);

/// <summary>Runs the <c>git</c> command line without a window, prompts or optional locks.</summary>
public static class GitRunner
{
    /// <summary>Output beyond this is cut; agents can produce huge generated files.</summary>
    public const int MaxOutputChars = 2 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static Task<GitOutput> RunAsync(string directory, params string[] args) => RunAsync(directory, args, [0]);

    public static async Task<GitOutput> RunAsync(string directory, string[] args, int[] okExitCodes, TimeSpan? timeout = null)
    {
        if (!Directory.Exists(directory)) return new(false, "", "The folder doesn't exist.");
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
        // No index refresh lock fighting the agent's own git commands, no credential prompts, paths unquoted
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
            return new(false, "", "git wasn't found. Install Git for Windows.");
        }
        using (process)
        {
            using var cts = new CancellationTokenSource(timeout ?? Timeout);
            var stdout = ReadCapped(process.StandardOutput);
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new(false, "", "git didn't answer in time.");
            }
            var output = await stdout;
            if (okExitCodes.Contains(process.ExitCode)) return new(true, output, null);
            var message = (await stderr).Trim();
            return new(false, "", message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                ? "Not a git repository."
                : message.Length > 0 ? CleanMessage(message) : $"git exited with code {process.ExitCode}.");
        }
    }

    // git's errors start with "fatal: " / "error: "; the first meaningful line is enough
    private static string CleanMessage(string stderr)
    {
        var line = stderr.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("hint:")) ?? stderr;
        foreach (var prefix in new[] { "fatal: ", "error: " })
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line[prefix.Length..];
        return line;
    }

    /// <summary>Reads at most <see cref="MaxOutputChars"/>; the rest is drained so git doesn't block on a full pipe.</summary>
    private static async Task<string> ReadCapped(StreamReader reader)
    {
        var sb = new StringBuilder();
        var buffer = new char[64 * 1024];
        int n;
        var truncated = false;
        while ((n = await reader.ReadAsync(buffer)) > 0)
        {
            if (sb.Length < MaxOutputChars) sb.Append(buffer, 0, Math.Min(n, MaxOutputChars - sb.Length));
            else truncated = true;
        }
        if (truncated) sb.Append("\n\\ Output cut: larger than 2 MB.\n");
        return sb.ToString();
    }
}
