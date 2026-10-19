using System.IO;

namespace KovanTerm.Services;

/// <summary>Çalıştırılabilir dosyaları PATH/PATHEXT üzerinden bulur ve ConPTY'ye verilecek komut satırını kurar.</summary>
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

    /// <summary>Dosyanın tam yolunu döndürür; bulunamazsa <c>null</c>.</summary>
    public string? Resolve(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;

        // Tam ya da göreli yol verilmişse PATH'te arama
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
    /// ConPTY'ye verilecek komut satırı. .cmd/.bat dosyaları doğrudan başlatılamadığı için
    /// <c>cmd.exe /k</c> ile sarılır; ajan kapansa da terminal kullanılabilir kalır.
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

    /// <summary>Komut satırındaki ilk öğe (çalıştırılabilir dosya); tırnaklı yolları destekler.</summary>
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
        // Uzantı zaten verilmişse önce olduğu gibi dene
        if (Path.HasExtension(pathWithoutExt))
            yield return pathWithoutExt;
        foreach (var ext in _extensions)
            yield return pathWithoutExt + ext.ToLowerInvariant();
    }
}
