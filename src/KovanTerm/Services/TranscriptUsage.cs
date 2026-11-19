using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace KovanTerm.Services;

/// <summary>
/// How much of the model's context a Claude Code conversation uses, read from its transcript (<c>.jsonl</c>):
/// the input of the latest assistant reply (<c>input_tokens + cache_creation_input_tokens + cache_read_input_tokens</c>).
/// Reads incrementally: each call only reads what was appended since the last one. One instance per node.
/// Also keeps the conversation's latest titles: <c>/rename</c>'s (<c>custom-title</c>) and Claude's own (<c>ai-title</c>).
/// </summary>
public sealed class TranscriptUsage
{
    /// <summary>A resumed conversation can be large; on the first read only its tail is needed.</summary>
    public const int MaxFirstRead = 2 * 1024 * 1024;

    private string? _path;
    private long _offset;

    /// <summary>Context tokens of the latest reply; <c>null</c> until one is seen.</summary>
    public long? ContextTokens { get; private set; }

    /// <summary>The latest <c>/rename</c> of the conversation; <c>null</c> if it was never renamed.</summary>
    public string? CustomTitle { get; private set; }

    /// <summary>The title Claude Code gave the conversation; <c>null</c> until it has one.</summary>
    public string? AiTitle { get; private set; }

    /// <summary>Reads what's new in the transcript; returns <c>true</c> if the context size or a title changed.</summary>
    public bool Update(string path)
    {
        if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
        {
            // A new conversation (/clear, resume of another one)
            _path = path;
            _offset = 0;
            ContextTokens = null;
            CustomTitle = AiTitle = null;
        }
        var before = (ContextTokens, CustomTitle, AiTitle);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _offset) _offset = 0; // rewritten
            var start = _offset == 0 && stream.Length > MaxFirstRead ? stream.Length - MaxFirstRead : _offset;
            var skipPartialFirstLine = start != _offset;
            stream.Position = start;
            var bytes = new byte[stream.Length - start];
            stream.ReadExactly(bytes);

            // Only complete lines; the last one may still be being written
            var end = Array.LastIndexOf(bytes, (byte)'\n');
            if (end < 0) return false;
            var from = skipPartialFirstLine ? Array.IndexOf(bytes, (byte)'\n') + 1 : 0;
            foreach (var line in Encoding.UTF8.GetString(bytes, from, end + 1 - from).Split('\n'))
            {
                if (ContextOf(line) is { } tokens) ContextTokens = tokens;
                // Later rows win: the latest title is the current one
                else if (TitleOf(line) is { } t)
                {
                    if (t.Custom) CustomTitle = t.Text;
                    else AiTitle = t.Text;
                }
            }
            _offset = start + end + 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return (ContextTokens, CustomTitle, AiTitle) != before;
    }

    /// <summary>A title row (<c>custom-title</c> or <c>ai-title</c>); <c>null</c> for anything else.</summary>
    public static (bool Custom, string Text)? TitleOf(string line)
    {
        var custom = line.Contains("\"custom-title\"", StringComparison.Ordinal);
        if (!custom && !line.Contains("\"ai-title\"", StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) return null;
            var (expected, key) = custom ? ("custom-title", "customTitle") : ("ai-title", "aiTitle");
            if (type.GetString() != expected || !root.TryGetProperty(key, out var v) || v.GetString() is not { } text) return null;
            text = ClaudeConversations.Shorten(text);
            return text.Length == 0 ? null : (custom, text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Context tokens of one transcript line if it's an assistant reply with usage; otherwise <c>null</c>.</summary>
    public static long? ContextOf(string line)
    {
        // Cheap filter before parsing: most lines are tool results or user messages
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal) || !line.Contains("\"usage\"", StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "assistant") return null;
            // Subagent turns are in the main transcript too (isSidechain); they don't fill the main context
            if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("message", out var message) || !message.TryGetProperty("usage", out var usage)) return null;
            return Long(usage, "input_tokens") + Long(usage, "cache_creation_input_tokens") + Long(usage, "cache_read_input_tokens");
        }
        catch (JsonException)
        {
            return null;
        }

        static long Long(JsonElement o, string key) =>
            o.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;
    }

    /// <summary>"12.3k", "399k", "1.2M".</summary>
    public static string Format(long tokens) => tokens switch
    {
        < 1000 => tokens.ToString(CultureInfo.InvariantCulture),
        < 100_000 => string.Create(CultureInfo.InvariantCulture, $"{tokens / 1000.0:0.#}k"),
        < 1_000_000 => string.Create(CultureInfo.InvariantCulture, $"{tokens / 1000}k"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{tokens / 1_000_000.0:0.#}M"),
    };
}
