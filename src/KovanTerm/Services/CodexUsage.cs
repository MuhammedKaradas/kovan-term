using System.IO;
using System.Text;
using System.Text.Json;

namespace KovanTerm.Services;

/// <summary>
/// The Codex plan's limits, from the <c>rate_limits</c> Codex writes into its own session files
/// (<c>~/.codex/sessions/YYYY/MM/DD/*.jsonl</c>, <c>CODEX_HOME</c> moves it) after each response. Read locally: no
/// request and no token. They're as fresh as the latest Codex response; a window whose reset time has passed shows 0 %.
/// </summary>
public sealed class CodexUsage(string? sessionsDirectory = null)
{
    private readonly string _sessions = sessionsDirectory ?? Path.Combine(
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
        "sessions");

    /// <summary>Only this much of a session's end is read: the latest record is there.</summary>
    private const int TailBytes = 256 * 1024;

    /// <summary>The plan's limits and its name (e.g. "plus"); empty when Codex hasn't recorded any.</summary>
    public (IReadOnlyList<UsageLimit> Limits, string? Plan) Read(DateTimeOffset now)
    {
        foreach (var file in NewestSessions().Take(5))
        {
            if (Latest(ReadTail(file)) is { } record) return Parse(record, now);
        }
        return ([], null);
    }

    /// <summary>Session files, newest first, from the newest day folders only.</summary>
    private IEnumerable<string> NewestSessions()
    {
        if (!Directory.Exists(_sessions)) return [];
        try
        {
            // YYYY/MM/DD folders sort by name; a long history stays cheap
            var days = Directory.EnumerateDirectories(_sessions).OrderDescending(StringComparer.Ordinal)
                .SelectMany(y => Directory.EnumerateDirectories(y).OrderDescending(StringComparer.Ordinal))
                .SelectMany(m => Directory.EnumerateDirectories(m).OrderDescending(StringComparer.Ordinal))
                .Take(3);
            return days.SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl"))
                .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string ReadTail(string path)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, s.Length - TailBytes);
            s.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(s, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>The last <c>rate_limits</c> object in the text, as JSON; <c>null</c> if there's none.</summary>
    public static string? Latest(string text)
    {
        const string key = "\"rate_limits\":{";
        var at = text.LastIndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;
        var start = at + key.Length - 1;
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return text[start..(i + 1)];
        }
        return null;
    }

    public static (IReadOnlyList<UsageLimit> Limits, string? Plan) Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var limits = new List<UsageLimit>();
            foreach (var key in new[] { "primary", "secondary" })
            {
                if (!root.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object
                    || !w.TryGetProperty("used_percent", out var p) || !p.TryGetDouble(out var percent))
                    continue;
                DateTimeOffset? resets = w.TryGetProperty("resets_at", out var r) && r.TryGetInt64(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;
                var minutes = w.TryGetProperty("window_minutes", out var m) && m.TryGetInt32(out var wm) ? wm : 0;
                // Recorded before the window reset: it has started over since
                if (resets is { } at && at <= now) (percent, resets) = (0, null);
                limits.Add(new UsageLimit(WindowLabel(minutes, key), percent, resets, null));
            }
            var plan = root.TryGetProperty("plan_type", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null;
            return (limits, plan);
        }
        catch (JsonException)
        {
            return ([], null);
        }
    }

    /// <summary>"5h", "Week", "Month"… from the window's length.</summary>
    public static string WindowLabel(int minutes, string fallback) => minutes switch
    {
        <= 0 => fallback == "primary" ? "Limit" : "Limit 2",
        < 24 * 60 => $"{minutes / 60}h",
        7 * 24 * 60 => "Week",
        >= 28 * 24 * 60 and <= 31 * 24 * 60 => "Month",
        _ => $"{Math.Round(minutes / 1440.0)}d",
    };
}
