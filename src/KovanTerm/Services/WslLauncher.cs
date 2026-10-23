using System.Diagnostics;
using System.IO;
using System.Text;

namespace KovanTerm.Services;

/// <summary>Lists installed WSL distros and builds the <c>wsl.exe</c> command line that starts a profile inside one.</summary>
public sealed class WslLauncher
{
    /// <summary>Linux home directory; passed to <c>wsl.exe --cd</c> as-is.</summary>
    public const string HomeDirectory = "~";

    private readonly Func<string> _readDistroList;
    private IReadOnlyList<string>? _distros;

    /// <summary>Full path of <c>wsl.exe</c>, or <c>null</c> if WSL isn't installed.</summary>
    public string? WslPath { get; }

    public WslLauncher(CommandResolver resolver, Func<string>? readDistroList = null)
    {
        WslPath = resolver.Resolve("wsl.exe");
        _readDistroList = readDistroList ?? ReadDistroList;
    }

    /// <summary>Installed distros (Docker Desktop's internal ones excluded); read once.</summary>
    public IReadOnlyList<string> Distros => _distros ??= WslPath == null ? [] : ParseDistroList(_readDistroList());

    public bool IsInstalled(string? distro) =>
        WslPath != null && (string.IsNullOrEmpty(distro) || Distros.Contains(distro, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> ParseDistroList(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Trim('\0'))
            .Where(l => l.Length > 0 && !l.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Command line passed to ConPTY. Without an executable the distro's login shell opens. Otherwise the command
    /// runs in an interactive login shell, so PATH entries from <c>~/.profile</c>/<c>~/.bashrc</c> (<c>~/.local/bin</c>,
    /// nvm…) apply; plain <c>wsl.exe -- claude</c> could pick up the Windows CLI from the appended Windows PATH.
    /// A login shell opens after the command exits, so the terminal stays usable.
    /// </summary>
    public static string BuildCommandLine(string wslPath, string? distro, string directory, string executable, string arguments)
    {
        var sb = new StringBuilder(Quote(wslPath));
        if (!string.IsNullOrEmpty(distro)) sb.Append(" -d ").Append(Quote(distro));
        sb.Append(" --cd ").Append(Quote(directory));
        if (string.IsNullOrWhiteSpace(executable)) return sb.ToString();

        var command = string.IsNullOrWhiteSpace(arguments) ? executable : $"{executable} {arguments}";
        var script = $"command -v {ShellQuote(executable)} >/dev/null 2>&1"
            + $" || echo {ShellQuote($"Kovan: {executable} not found on the login shell PATH of this distro.")}; "
            + $"{command}; exec \"${{SHELL:-/bin/sh}}\" -l";
        // After "--" wsl.exe hands the rest to the user's shell unchanged
        return sb.Append($" -- exec \"${{SHELL:-/bin/sh}}\" -lic {ShellQuote(script)}").ToString();
    }

    /// <summary>Whether <paramref name="directory"/> is a Linux path (<c>~</c> or absolute) rather than a Windows one.</summary>
    public static bool IsLinuxPath(string directory) => directory == HomeDirectory || directory.StartsWith('/');

    private readonly Dictionary<string, Task<bool>> _starting = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Boots the distro if it's stopped and waits until systemd (if the distro uses it) has finished starting, before
    /// its terminals start. While systemd is still "starting", WSL holds new login sessions, so a terminal would stay
    /// blank with no explanation; a slow or hanging service (e.g. a database) can take minutes. <c>--exec</c> isn't held.
    /// Gives up after a few minutes and lets the terminal start anyway. Starting the same distro twice shares one wait.
    /// </summary>
    /// <summary>Waits for systemd to leave the "starting" state; does nothing on distros without systemd.</summary>
    public const string WaitForBootScript =
        "[ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1 && timeout 240 systemctl is-system-running --wait >/dev/null 2>&1; exit 0";

    public Task<bool> StartDistroAsync(string? distro)
    {
        if (WslPath is not { } wsl) return Task.FromResult(false);
        var key = distro ?? "";
        lock (_starting)
        {
            if (_starting.TryGetValue(key, out var running)) return running;
            return _starting[key] = Task.Run(async () =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo(wsl)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // Not inherited from Kovan (which may itself have redirected handles)
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                if (distro != null) { psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(distro); }
                psi.ArgumentList.Add("--exec");
                psi.ArgumentList.Add("sh");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(WaitForBootScript);
                try
                {
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    await p.WaitForExitAsync(timeout.Token);
                    lock (_starting) _starting.Remove(key); // a later node checks again (the distro may have stopped)
                    return p.ExitCode == 0;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
                {
                    lock (_starting) _starting.Remove(key); // try again next time
                    return false;
                }
            });
        }
    }

    /// <summary><c>/home/me</c> → <c>\\wsl.localhost\Debian\home\me</c>, so Windows can check or browse it.</summary>
    public static string ToUncPath(string distro, string linuxPath) =>
        $@"\\wsl.localhost\{distro}{linuxPath.Replace('/', '\\')}";

    /// <summary><c>\\wsl.localhost\Debian\home\me</c> (or <c>\\wsl$\…</c>) → <c>/home/me</c>; <c>null</c> for other paths.</summary>
    public static string? FromUncPath(string path, string distro)
    {
        foreach (var root in new[] { @"\\wsl.localhost\", @"\\wsl$\" })
        {
            var prefix = root + distro;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = path[prefix.Length..];
            if (rest.Length > 0 && rest[0] != '\\') continue;
            var linux = rest.Replace('\\', '/').TrimEnd('/');
            return linux.Length == 0 ? "/" : linux;
        }
        return null;
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    private static string ShellQuote(string s) => "'" + s.Replace("'", @"'\''") + "'";

    private static string ReadDistroList()
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe", "--list --quiet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            // Otherwise wsl.exe writes UTF-16
            psi.Environment["WSL_UTF8"] = "1";
            using var process = Process.Start(psi);
            if (process == null) return "";
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return "";
            }
            return process.ExitCode == 0 ? output.Result : "";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Debug.WriteLine($"Couldn't list WSL distros: {ex.Message}");
            return "";
        }
    }
}
