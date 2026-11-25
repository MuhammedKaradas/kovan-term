using System.IO;

namespace KovanTerm.Services;

/// <summary>
/// Claude Code without permission prompts (#140): everywhere (Settings) or in folders the user trusts (New terminal),
/// by adding <c>--permission-mode bypassPermissions</c> unless the profile already names a permission mode.
/// </summary>
public static class PermissionBypass
{
    public const string Arguments = "--permission-mode bypassPermissions";

    /// <summary>Does this folder skip prompts: everywhere, or it (or its repository) is in the list, or inside one that is?</summary>
    public static bool AppliesTo(bool everywhere, IEnumerable<string> folders, string? directory, string? repoRoot = null)
    {
        if (everywhere) return true;
        foreach (var folder in folders)
            if (IsWithin(directory, folder) || IsWithin(repoRoot, folder)) return true;
        return false;
    }

    /// <summary>Do the profile's arguments decide about permissions themselves?</summary>
    public static bool NamesPermissionMode(string arguments) =>
        arguments.Contains("--permission-mode", StringComparison.OrdinalIgnoreCase)
        || arguments.Contains("--dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase);

    /// <summary>Was the node started without prompts (its header shows it)?</summary>
    public static bool IsOn(string? commandLine) =>
        commandLine != null && (commandLine.Contains("bypassPermissions", StringComparison.OrdinalIgnoreCase)
                                || commandLine.Contains("--dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase));

    /// <summary>The list with <paramref name="folder"/> added or taken out.</summary>
    public static List<string> With(IEnumerable<string> folders, string folder, bool on)
    {
        var list = folders.Where(f => !Same(f, folder)).ToList();
        if (on) list.Add(Normalize(folder));
        return list;
    }

    private static bool IsWithin(string? directory, string folder)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(folder)) return false;
        var d = Normalize(directory);
        var f = Normalize(folder);
        return d.Equals(f, StringComparison.OrdinalIgnoreCase)
               || d.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || d.StartsWith(f + '/', StringComparison.Ordinal);
    }

    private static bool Same(string a, string b) => Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

    // Linux paths (WSL) as they are; Windows paths full and without a trailing separator
    private static string Normalize(string path) =>
        WslLauncher.IsLinuxPath(path) ? (path.Length > 1 ? path.TrimEnd('/') : path)
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
