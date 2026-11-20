using System.IO;
using System.Text.Json;

namespace KovanTerm.Services;

/// <summary>
/// Antigravity conversations, so a node can resume its own. Antigravity can't be given an id up front, but it keeps
/// each folder's latest conversation in <c>~/.gemini/antigravity-cli/cache/last_conversations.json</c> (updated after
/// a turn), and resumes one with <c>--conversation &lt;id&gt;</c>. Kovan only reads these files.
/// </summary>
public sealed class AntigravityConversations(string? home = null)
{
    private readonly string _home = home ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli");

    /// <summary>The folder's latest conversation id; <c>null</c> if there's none or the file can't be read.</summary>
    public string? Latest(string folder)
    {
        var path = Path.Combine(_home, "cache", "last_conversations.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var wanted = Normalize(folder);
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(Normalize(entry.Name), wanted, StringComparison.OrdinalIgnoreCase)
                    && entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { } id && Guid.TryParse(id, out _))
                    return id;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    /// <summary>Is the conversation still on disk (<c>conversations/&lt;id&gt;.db</c>)?</summary>
    public bool Exists(string id) =>
        Guid.TryParse(id, out _) && File.Exists(Path.Combine(_home, "conversations", id + ".db"));

    /// <summary>
    /// <c>--conversation &lt;id&gt;</c> when the conversation exists; empty when it's gone (a new one starts) or the
    /// profile picks one itself.
    /// </summary>
    public string Arguments(string id, string profileArguments)
    {
        var words = profileArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w is "-c" or "--continue" or "--conversation" || w.StartsWith("--conversation=", StringComparison.Ordinal)))
            return "";
        return Exists(id) ? $"--conversation {id}" : "";
    }

    private static string Normalize(string folder)
    {
        try { folder = Path.GetFullPath(folder.Replace('/', '\\')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return folder.TrimEnd('\\');
    }
}
