using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>A Claude Code conversation saved on disk.</summary>
/// <param name="SessionId">Its id (the transcript's file name); <c>claude --resume &lt;id&gt;</c> reopens it.</param>
/// <param name="Folder">The folder it ran in; <c>null</c> if the transcript doesn't say.</param>
/// <param name="Title">The user's <c>/rename</c>, else Claude's generated title, else the first prompt.</param>
public sealed record ConversationInfo(string SessionId, string? Folder, string Title, DateTime LastActive, string TranscriptPath);

/// <summary>
/// Lists Claude Code's saved conversations (<c>~/.claude/projects/*/*.jsonl</c>) to reopen them as nodes. Transcripts
/// can be large (hundreds of MB in total), so only each file's start (folder, first prompt) and end (latest title)
/// are read.
/// </summary>
public sealed partial class ClaudeConversations
{
    private const int HeadBytes = 128 * 1024;
    private const int TailBytes = 64 * 1024;
    private const int MaxTitle = 120;

    private readonly string _projectsDirectory;

    public ClaudeConversations(string? projectsDirectory = null)
    {
        _projectsDirectory = projectsDirectory ?? Path.Combine(
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } config
                ? config
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            "projects");
    }

    /// <summary>The most recently active conversations, newest first. Run it off the UI thread.</summary>
    public IReadOnlyList<ConversationInfo> List(int max = 300)
    {
        if (!Directory.Exists(_projectsDirectory)) return [];
        IEnumerable<FileInfo> files;
        try
        {
            // Only conversations: subagent transcripts live in subfolders
            files = new DirectoryInfo(_projectsDirectory).EnumerateDirectories()
                .SelectMany(d => d.EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
                .Where(f => Guid.TryParse(Path.GetFileNameWithoutExtension(f.Name), out _))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(max)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        var result = new List<ConversationInfo>();
        foreach (var file in files)
            if (Read(file) is { } info) result.Add(info);
        return result;
    }

    /// <summary>One conversation's folder and title; <c>null</c> if it has no prompt (e.g. opened and closed).</summary>
    public static ConversationInfo? Read(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = ReadLines(stream, 0, HeadBytes, skipFirst: false);
            var tail = stream.Length > HeadBytes
                ? ReadLines(stream, Math.Max(HeadBytes, stream.Length - TailBytes), TailBytes, skipFirst: true)
                : [];
            return Parse(Path.GetFileNameWithoutExtension(file.Name), head, tail, file.LastWriteTime, file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Builds the info from the transcript's first and last lines.</summary>
    public static ConversationInfo? Parse(string sessionId, IReadOnlyList<string> head, IReadOnlyList<string> tail,
        DateTime lastActive, string path)
    {
        string? folder = null, firstPrompt = null, aiTitle = null, customTitle = null;
        foreach (var line in head.Concat(tail))
        {
            if (Json(line) is not { } o) continue;
            var type = Str(o, "type");
            folder ??= Str(o, "cwd");
            if (type == "user" && firstPrompt == null && !Bool(o, "isMeta") && !Bool(o, "isSidechain"))
                firstPrompt = PromptText(o);
            // Later rows win: the latest title is the current one
            if (type == "ai-title" && Str(o, "aiTitle") is { Length: > 0 } ai) aiTitle = ai;
            if (type == "custom-title" && Str(o, "customTitle") is { Length: > 0 } custom) customTitle = custom;
        }
        var title = customTitle ?? aiTitle ?? firstPrompt;
        return title == null ? null : new ConversationInfo(sessionId, folder, Shorten(title), lastActive, path);
    }

    private static List<string> ReadLines(FileStream stream, long from, int count, bool skipFirst)
    {
        stream.Position = from;
        var buffer = new byte[(int)Math.Min(count, stream.Length - from)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var lines = Encoding.UTF8.GetString(buffer, 0, read).Split('\n').ToList();
        // Lines cut by the window's edges aren't valid JSON; drop the partial first (when starting mid-file) and last
        if (skipFirst && lines.Count > 0) lines.RemoveAt(0);
        if (lines.Count > 0 && read == count) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static JsonElement? Json(string line)
    {
        if (line.Length < 2 || line[0] != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement o, string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"<[^>]{1,200}>")]
    private static partial Regex Tag();

    /// <summary>The prompt's text without pasted-content and command tags; <c>null</c> for tool results or empty prompts.</summary>
    public static string? PromptText(JsonElement user)
    {
        if (!user.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content)) return null;
        var text = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join(" ", content.EnumerateArray()
                .Where(i => Str(i, "type") == "text").Select(i => Str(i, "text"))),
            _ => null,
        };
        if (text == null) return null;
        // Slash commands and system reminders are wrapped in tags; a prompt that's only tags isn't one the user typed
        var plain = Tag().Replace(text, " ").Trim();
        return plain.Length == 0 || text.TrimStart().StartsWith("<command-", StringComparison.Ordinal) ? null : plain;
    }

    /// <summary>One line, at most <see cref="MaxTitle"/> characters.</summary>
    public static string Shorten(string s)
    {
        var one = string.Join(' ', s.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim())).Trim();
        return one.Length > MaxTitle ? one[..MaxTitle] + "…" : one;
    }
}
