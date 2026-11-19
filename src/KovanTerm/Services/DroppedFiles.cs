using System.Text;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>
/// Files dropped on a terminal become their paths, typed into it: an agent such as Claude Code attaches an image whose
/// path it's given. Paths are quoted for the terminal's shell, and a WSL terminal gets Linux paths.
/// </summary>
public static partial class DroppedFiles
{
    /// <summary>The text to paste for <paramref name="paths"/>, ending in a space so typing can go on.</summary>
    /// <param name="distro">The WSL distro the terminal runs in; <c>null</c> for a Windows terminal.</param>
    public static string Format(IEnumerable<string> paths, bool wsl, string? distro)
    {
        var parts = paths.Select(Clean).Where(p => p.Length > 0)
            .Select(p => wsl ? QuoteForShell(ToLinux(p, distro)) : QuoteForWindows(p)).ToList();
        return parts.Count == 0 ? "" : string.Join(' ', parts) + " ";
    }

    /// <summary><c>C:\shots\a.png</c> → <c>/mnt/c/shots/a.png</c>; a file inside the distro → its own path.</summary>
    public static string ToLinux(string path, string? distro)
    {
        if (distro != null && WslLauncher.FromUncPath(path, distro) is { } inside) return inside;
        // Another distro's file, or a network share: as Linux sees the UNC form
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path.Replace('\\', '/');
        return ClaudeHooks.ToWslPath(path);
    }

    /// <summary>Quotes a path with spaces or characters cmd and PowerShell would read (<c>&amp; ( ) ; ,</c> …).</summary>
    public static string QuoteForWindows(string path) => WindowsSpecial().IsMatch(path) ? $"\"{path}\"" : path;

    /// <summary>Single quotes unless the path is plain: <c>$</c>, <c>!</c>, spaces and quotes are taken literally.</summary>
    public static string QuoteForShell(string path) => PlainShell().IsMatch(path) ? path : WslLauncher.ShellQuote(path);

    /// <summary>Without control characters: a newline in a file name would run what's typed so far.</summary>
    public static string Clean(string path)
    {
        var s = new StringBuilder(path.Length);
        foreach (var c in path)
            if (!char.IsControl(c)) s.Append(c);
        return s.ToString().Trim();
    }

    [GeneratedRegex(@"[\s&()\[\]{}^=;!'+,`~$%@#]")] private static partial Regex WindowsSpecial();
    [GeneratedRegex(@"^[A-Za-z0-9_./:@%+,=-]+$")] private static partial Regex PlainShell();
}
