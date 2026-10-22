using System.IO;

namespace KovanTerm.Services;

/// <summary>Finds executables via PATH/PATHEXT and builds the command line passed to ConPTY.</summary>
public sealed class CommandResolver
{
    private readonly string[] _searchDirs;
    private readonly string[] _extensions;

    public CommandResolver(string? path = null, string? pathExt = null)
    {
        _searchDirs = (path ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _extensions = (pathExt ?? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Returns the file's full path, or <c>null</c> if not found.</summary>
    public string? Resolve(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;

        // If a full or relative path is given, don't search PATH
        if (executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
            return WithExtensions(executable).FirstOrDefault(File.Exists);

        foreach (var dir in _searchDirs)
        {
            var match = WithExtensions(Path.Combine(dir, executable)).FirstOrDefault(File.Exists);
            if (match != null) return match;
        }
        return null;
    }

    /// <summary>
    /// Command line passed to ConPTY. Since .cmd/.bat files can't be started directly, they're
    /// wrapped in <c>cmd.exe /k</c>; the terminal stays usable even after the agent exits.
    /// </summary>
    public static string BuildCommandLine(string resolvedPath, string arguments)
    {
        var ext = Path.GetExtension(resolvedPath);
        var quoted = resolvedPath.Contains(' ') ? $"\"{resolvedPath}\"" : resolvedPath;
        var line = string.IsNullOrWhiteSpace(arguments) ? quoted : $"{quoted} {arguments}";
        return ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            ? $"cmd.exe /k \"{line}\""
            : line;
    }

    /// <summary>First token of the command line (the executable); supports quoted paths.</summary>
    public static string FirstToken(string commandLine)
    {
        var s = commandLine.TrimStart();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end > 0 ? s[1..end] : s.Trim('"');
        }
        var space = s.IndexOf(' ');
        return space > 0 ? s[..space] : s;
    }

    private IEnumerable<string> WithExtensions(string pathWithoutExt)
    {
        // If an extension is already given, try it as-is first
        if (Path.HasExtension(pathWithoutExt))
            yield return pathWithoutExt;
        foreach (var ext in _extensions)
            yield return pathWithoutExt + ext.ToLowerInvariant();
    }
}
