using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>One message of a conversation, shortened for a preview.</summary>
public sealed record PreviewMessage(bool FromUser, string Text);

/// <summary>
/// The last messages of a Claude Code conversation, read from the end of its transcript: what was asked and the
/// answers' text, without tool calls, tool results, reminders or slash commands. Shown on a node that hasn't started.
/// </summary>
public static partial class ConversationPreview
{
    /// <summary>A long conversation's beginning isn't needed.</summary>
    public const int TailBytes = 512 * 1024;
    private const int MaxChars = 600;

    public static IReadOnlyList<PreviewMessage> Read(string transcriptPath, int count = 8)
    {
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Position = start;
            var bytes = new byte[stream.Length - start];
            stream.ReadExactly(bytes);
            var text = Encoding.UTF8.GetString(bytes);
            // A partial first line, when reading from the middle
            if (start > 0 && text.IndexOf('\n') is var nl and >= 0) text = text[(nl + 1)..];
            return Parse(text.Split('\n'), count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<PreviewMessage> Parse(IEnumerable<string> lines, int count)
    {
        var messages = new List<PreviewMessage>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line.Length > 256 * 1024 || !(line.Contains("\"user\"") || line.Contains("\"assistant\""))) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var o = doc.RootElement;
                if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("type", out var t)) continue;
                var type = t.GetString();
                if (type is not ("user" or "assistant")) continue;
                if (o.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) continue;
                if (o.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) continue;
                if (!o.TryGetProperty("message", out var message) || Text(message) is not { } text) continue;
                messages.Add(new PreviewMessage(type == "user", text));
            }
            catch (JsonException) { }
        }
        return messages.Count > count ? messages[^count..] : messages;
    }

    private static string? Text(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content)) return null;
        string? text = null;
        if (content.ValueKind == JsonValueKind.String) text = content.GetString();
        else if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var bt)) continue;
                // A tool result isn't something said
                if (bt.GetString() == "tool_result") return null;
                if (bt.GetString() == "text" && block.TryGetProperty("text", out var s)) parts.Add(s.GetString() ?? "");
            }
            text = string.Join("\n", parts);
        }
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.StartsWith('<')) return null;
        text = Blank().Replace(text, "\n\n");
        return text.Length > MaxChars ? text[..MaxChars].TrimEnd() + "…" : text;
    }

    [GeneratedRegex(@"\n{3,}")] private static partial Regex Blank();
}
