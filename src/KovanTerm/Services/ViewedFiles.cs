using System.IO;

namespace KovanTerm.Services;

/// <summary>
/// Files the user marked as viewed in the Changes panel (#142), per folder and path, with what the file looked like
/// then (size and last write time). A file that changed since is no longer viewed. Kept for this run of Kovan.
/// </summary>
public sealed class ViewedFiles
{
    private readonly Dictionary<string, string> _viewed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The file as it is now: its status, size and last write; "gone" for a deleted one.</summary>
    public static string Fingerprint(string folder, ChangedFile file)
    {
        try
        {
            var info = new FileInfo(Path.Combine(folder, file.Path));
            return info.Exists ? $"{file.Code}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : $"{file.Code}:gone";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"{file.Code}:?";
        }
    }

    private static string Key(string folder, string path) => Path.TrimEndingDirectorySeparator(folder) + "|" + path;

    /// <summary>Is it marked and unchanged since? A changed file loses its mark.</summary>
    public bool IsViewed(string folder, ChangedFile file, string fingerprint)
    {
        var key = Key(folder, file.Path);
        if (!_viewed.TryGetValue(key, out var seen)) return false;
        if (seen == fingerprint) return true;
        _viewed.Remove(key);
        return false;
    }

    public void Set(string folder, ChangedFile file, string fingerprint, bool viewed)
    {
        if (viewed) _viewed[Key(folder, file.Path)] = fingerprint;
        else _viewed.Remove(Key(folder, file.Path));
    }
}
