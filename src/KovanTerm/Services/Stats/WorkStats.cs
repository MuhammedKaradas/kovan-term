using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KovanTerm.Services.Stats;

/// <summary>One stretch of continuous work (local time): events no more than <see cref="WorkStats.IdleGap"/> apart.</summary>
public sealed record WorkSpan(DateTime Start, DateTime End)
{
    [JsonIgnore] public TimeSpan Duration => End - Start;
}

/// <summary>Tokens a model read and wrote.</summary>
/// <param name="CacheRead">Context read back from the prompt cache: most of a long conversation's tokens.</param>
public sealed record WorkTokens(long Input = 0, long Output = 0, long CacheWrite = 0, long CacheRead = 0)
{
    [JsonIgnore] public long Total => Input + Output + CacheWrite + CacheRead;
    public static readonly WorkTokens Zero = new();
    public static WorkTokens operator +(WorkTokens a, WorkTokens b) =>
        new(a.Input + b.Input, a.Output + b.Output, a.CacheWrite + b.CacheWrite, a.CacheRead + b.CacheRead);
}

/// <summary>Something the user asked, and when.</summary>
public sealed record WorkPrompt(DateTime Date, string Text);

/// <summary>A conversation's tokens with one model on one day.</summary>
public sealed record WorkTokenDay(DateOnly Day, string Model, WorkTokens Tokens);

/// <summary>One Claude Code or Codex conversation: where, what was asked, when it was active, what it cost.</summary>
/// <param name="Agent"><c>claude</c> or <c>codex</c>.</param>
public sealed record WorkSession(string Id, string Agent, string Cwd, string Title, IReadOnlyList<WorkPrompt> Prompts,
    IReadOnlyList<WorkSpan> Spans, IReadOnlyList<WorkTokenDay> TokenDays)
{
    [JsonIgnore] public DateTime Start => Spans.Count > 0 ? Spans[0].Start : DateTime.MinValue;
    [JsonIgnore] public DateTime End => Spans.Count > 0 ? Spans[^1].End : DateTime.MinValue;
    [JsonIgnore] public TimeSpan ActiveTime => TimeSpan.FromTicks(Spans.Sum(s => s.Duration.Ticks));

    /// <summary>The project: the folder's name.</summary>
    [JsonIgnore]
    public string Project =>
        string.Equals(Cwd.TrimEnd('\\', '/'), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase) ? "~"
        : Path.GetFileName(Cwd.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Cwd;

    public WorkTokens TokensIn(DateOnly from, DateOnly to) =>
        TokenDays.Where(d => d.Day >= from && d.Day < to).Aggregate(WorkTokens.Zero, (a, d) => a + d.Tokens);
}

/// <summary>
/// What was worked on, when and for how long, read from Claude Code's transcripts (<c>~/.claude/projects</c>, its
/// subagents' included) and Codex's sessions (<c>~/.codex/sessions</c>). Each file is read once and cached by size and
/// time; only the ones that changed are read again.
/// </summary>
public static partial class WorkStats
{
    /// <summary>Events further apart than this end a stretch of work: a break, not thinking time.</summary>
    public static readonly TimeSpan IdleGap = TimeSpan.FromMinutes(10);
    /// <summary>A stretch with a single event still took some time.</summary>
    public static readonly TimeSpan MinimumSpan = TimeSpan.FromMinutes(1);
    /// <summary>Bumped when what's read from a file changes, so cached readings are redone.</summary>
    public const int CacheVersion = 1;
    private const int MaxPrompts = 40, MaxPromptLength = 240;
    /// <summary>Lines longer than this are tool output: only their time and usage are taken, without parsing them.</summary>
    private const int BigLine = 64 * 1024;

    // ---- Claude Code ----

    public static WorkSession? ReadClaude(string path)
    {
        // Subagents keep their own transcripts beside it; their tokens are this conversation's
        var subagents = Path.Combine(Path.ChangeExtension(path, null), "subagents");
        var sub = Directory.Exists(subagents) ? Directory.EnumerateFiles(subagents, "*.jsonl").Select(Lines) : [];
        return ParseClaude(Path.GetFileNameWithoutExtension(path), Lines(path), sub);
    }

    public static WorkSession? ParseClaude(string id, IEnumerable<string> lines, IEnumerable<IEnumerable<string>> subagents)
    {
        var times = new List<DateTime>();
        var prompts = new List<WorkPrompt>();
        string? cwd = null, aiTitle = null, customTitle = null;
        // A reply streamed in parts repeats its usage on every part: one count per message
        var usage = new Dictionary<string, (DateTime When, string Model, WorkTokens Tokens)>();

        void Scan(IEnumerable<string> source, bool main)
        {
            foreach (var line in source)
            {
                if (line.Length == 0) continue;
                if (line.Length > BigLine)
                {
                    if (LastTimestamp(line) is not { } when) continue;
                    if (main) times.Add(when);
                    if (BigLineUsage(line) is { } big) usage[big.Id] = (when, big.Model, big.Tokens);
                    continue;
                }
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }
                using (doc)
                {
                    var o = doc.RootElement;
                    if (o.ValueKind != JsonValueKind.Object) continue;
                    var type = Str(o, "type");
                    if (type == "assistant" && o.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                        && message.TryGetProperty("usage", out var u) && Date(Str(o, "timestamp")) is { } at)
                    {
                        var key = Str(message, "id") ?? Str(o, "uuid") ?? Guid.NewGuid().ToString();
                        usage[key] = (at, Str(message, "model") ?? "claude", ClaudeTokens(u));
                    }
                    if (!main) continue;
                    switch (type)
                    {
                        case "ai-title": aiTitle = Str(o, "aiTitle") ?? aiTitle; break;
                        case "custom-title": customTitle = Str(o, "customTitle") ?? customTitle; break;
                        case "summary": aiTitle ??= Str(o, "summary"); break;
                        case "user" or "assistant":
                            // A subagent's side conversation is its parent's work, already timed there
                            if (o.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) continue;
                            if (Date(Str(o, "timestamp")) is not { } when) continue;
                            times.Add(when);
                            if (cwd == null && Str(o, "cwd") is { Length: > 0 } c) cwd = c;
                            if (type == "user" && prompts.Count < MaxPrompts && !(o.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
                                && o.TryGetProperty("message", out var m) && PromptText(m) is { } text)
                                prompts.Add(new WorkPrompt(when, text));
                            break;
                    }
                }
            }
        }

        Scan(lines, main: true);
        foreach (var s in subagents) Scan(s, main: false);
        if (cwd == null || times.Count == 0) return null;
        var title = customTitle ?? aiTitle ?? prompts.FirstOrDefault()?.Text ?? "Untitled session";
        return new WorkSession(id, "claude", cwd, ClaudeConversations.Shorten(title), prompts, Spans(times),
            ByDay(usage.Values.Select(v => (v.When, v.Model, v.Tokens))));
    }

    private static WorkTokens ClaudeTokens(JsonElement u) =>
        new(Long(u, "input_tokens"), Long(u, "output_tokens"), Long(u, "cache_creation_input_tokens"), Long(u, "cache_read_input_tokens"));

    /// <summary>The message id, model and usage of a line too big to parse: the usage comes after the content.</summary>
    private static (string Id, string Model, WorkTokens Tokens)? BigLineUsage(string line)
    {
        var at = line.LastIndexOf("\"usage\":{", StringComparison.Ordinal);
        if (at < 0) return null;
        var open = at + 8;
        int depth = 0, end = open;
        for (; end < line.Length; end++)
        {
            if (line[end] == '{') depth++;
            else if (line[end] == '}' && --depth == 0) break;
        }
        if (end >= line.Length) return null;
        try
        {
            using var doc = JsonDocument.Parse(line.AsMemory(open, end - open + 1));
            var id = IdOf().Match(line) is { Success: true } m ? m.Groups[1].Value : $"line-{line.Length}-{at}";
            var model = ModelOf().Match(line) is { Success: true } mm ? mm.Groups[1].Value : "claude";
            return (id, model, ClaudeTokens(doc.RootElement));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- Codex ----

    public static WorkSession? ReadCodex(string path) => ParseCodex(Path.GetFileNameWithoutExtension(path), Lines(path));

    public static WorkSession? ParseCodex(string id, IEnumerable<string> lines)
    {
        var times = new List<DateTime>();
        var prompts = new List<WorkPrompt>();
        string? cwd = null, model = null;
        var records = new List<(DateTime, string, WorkTokens)>();
        var counts = new List<(DateTime, string, WorkTokens)>();
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            if (line.Length > BigLine)
            {
                if (LastTimestamp(line) is { } big) times.Add(big);
                continue;
            }
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var o = doc.RootElement;
                if (o.ValueKind != JsonValueKind.Object || Date(Str(o, "timestamp")) is not { } when) continue;
                var type = Str(o, "type");
                if (!o.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) continue;
                var payloadType = Str(p, "type");
                switch (type)
                {
                    case "session_meta":
                        id = Str(p, "id") ?? id;
                        cwd ??= Str(p, "cwd");
                        continue;
                    case "turn_context":
                        cwd ??= Str(p, "cwd");
                        model = Str(p, "model") ?? model;
                        continue;
                    case "token_usage_record" when p.TryGetProperty("usage", out var u):
                        records.Add((when, model ?? "codex", CodexTokens(u)));
                        continue;
                }
                if (type == "event_msg" && payloadType == "thread_settings_applied"
                    && p.TryGetProperty("thread_settings", out var ts) && Str(ts, "model") is { } m)
                    model = m;
                if (type == "event_msg" && payloadType == "token_count" && p.TryGetProperty("info", out var info)
                    && info.ValueKind == JsonValueKind.Object && info.TryGetProperty("last_token_usage", out var last))
                    counts.Add((when, model ?? "codex", CodexTokens(last)));
                if (type is not ("response_item" or "event_msg")) continue;
                times.Add(when);
                if (prompts.Count < MaxPrompts && type == "event_msg" && payloadType == "item_completed"
                    && p.TryGetProperty("item", out var item) && Str(item, "type") == "UserMessage"
                    && item.TryGetProperty("content", out var content) && PromptText(content) is { } text)
                    prompts.Add(new WorkPrompt(when, text));
            }
        }
        if (cwd == null || times.Count == 0) return null;
        // Newer sessions record each response's usage; older ones only a running count per turn
        var usage = records.Count > 0 ? records : counts;
        return new WorkSession(id, "codex", cwd, ClaudeConversations.Shorten(prompts.FirstOrDefault()?.Text ?? "Untitled session"),
            prompts, Spans(times), ByDay(usage));
    }

    private static WorkTokens CodexTokens(JsonElement u)
    {
        var input = Long(u, "input_tokens");
        var cached = Long(u, "cached_input_tokens");
        return new WorkTokens(Math.Max(0, input - cached), Long(u, "output_tokens"), Long(u, "cache_write_input_tokens"), cached);
    }

    // ---- Shared ----

    /// <summary>What the user typed: text, not tool results, slash commands or the system's own notes.</summary>
    public static string? PromptText(JsonElement message)
    {
        var content = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c) ? c : message;
        string? text = null;
        if (content.ValueKind == JsonValueKind.String) text = content.GetString();
        else if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                var t = Str(block, "type");
                if (t == "tool_result") return null;
                if (t is "text" or "input_text" && Str(block, "text") is { } s) parts.Add(s);
            }
            text = string.Join(' ', parts);
        }
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.StartsWith('<')) return null;
        text = Pasted().Replace(text, "[pasted text]");
        text = Spaces().Replace(text, " ").Trim();
        return text.Length > MaxPromptLength ? text[..MaxPromptLength] + "…" : text;
    }

    /// <summary>Sorted event times as stretches of work.</summary>
    public static List<WorkSpan> Spans(List<DateTime> times)
    {
        times.Sort();
        var result = new List<WorkSpan>();
        foreach (var t in times)
        {
            if (result.Count > 0 && t - result[^1].End <= IdleGap)
                result[^1] = result[^1] with { End = t > result[^1].End ? t : result[^1].End };
            else
                result.Add(new WorkSpan(t, t));
        }
        return result.Select(s => s.Duration < MinimumSpan ? s with { End = s.Start + MinimumSpan } : s).ToList();
    }

    /// <summary>Spans as one timeline without overlaps: two sessions at once are one hour worked, not two.</summary>
    public static List<WorkSpan> Union(IEnumerable<WorkSpan> spans)
    {
        var result = new List<WorkSpan>();
        foreach (var s in spans.OrderBy(s => s.Start))
        {
            if (result.Count > 0 && s.Start <= result[^1].End)
                result[^1] = result[^1] with { End = s.End > result[^1].End ? s.End : result[^1].End };
            else
                result.Add(s);
        }
        return result;
    }

    /// <summary>How much of the spans falls on each day, local midnight to midnight.</summary>
    public static Dictionary<DateOnly, TimeSpan> PerDay(IEnumerable<WorkSpan> spans)
    {
        var result = new Dictionary<DateOnly, TimeSpan>();
        foreach (var s in spans)
        {
            var cursor = s.Start;
            while (cursor < s.End)
            {
                var next = cursor.Date.AddDays(1);
                var end = next < s.End ? next : s.End;
                var day = DateOnly.FromDateTime(cursor);
                result[day] = result.GetValueOrDefault(day) + (end - cursor);
                cursor = end;
            }
        }
        return result;
    }

    /// <summary>The parts of the spans inside [from, to).</summary>
    public static IEnumerable<WorkSpan> Clip(IEnumerable<WorkSpan> spans, DateTime from, DateTime to) =>
        spans.Select(s => new WorkSpan(s.Start > from ? s.Start : from, s.End < to ? s.End : to)).Where(s => s.End > s.Start);

    private static List<WorkTokenDay> ByDay(IEnumerable<(DateTime When, string Model, WorkTokens Tokens)> usage) =>
        usage.GroupBy(u => (Day: DateOnly.FromDateTime(u.When), u.Model))
            .Select(g => new WorkTokenDay(g.Key.Day, g.Key.Model, g.Aggregate(WorkTokens.Zero, (a, u) => a + u.Tokens)))
            .OrderBy(d => d.Day).ToList();

    private static DateTime? Date(string? s) =>
        s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.LocalDateTime : null;

    private static DateTime? LastTimestamp(string line)
    {
        var at = line.LastIndexOf("\"timestamp\":\"", StringComparison.Ordinal);
        if (at < 0) return null;
        var start = at + 13;
        var end = line.IndexOf('"', start);
        return end > start ? Date(line[start..end]) : null;
    }

    private static string? Str(JsonElement o, string key) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Long(JsonElement o, string key) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;

    /// <summary>A file's lines, read while it may still be written to; none if it can't be read.</summary>
    private static IEnumerable<string> Lines(string path)
    {
        StreamReader reader;
        try
        {
            reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        using (reader)
        {
            while (true)
            {
                string? line;
                try { line = reader.ReadLine(); }
                catch (IOException) { yield break; }
                if (line == null) yield break;
                yield return line;
            }
        }
    }

    // ---- Every file, with a cache ----

    /// <summary>Claude Code transcripts (not subagents', read with their parent) and Codex sessions.</summary>
    public static IEnumerable<(string Path, string Agent)> Files(string? claudeProjects = null, string? codexSessions = null)
    {
        claudeProjects ??= ClaudeSessions.DefaultProjectsDirectory;
        codexSessions ??= Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "sessions");
        if (Directory.Exists(claudeProjects))
            foreach (var folder in Directory.EnumerateDirectories(claudeProjects))
            foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl"))
                yield return (file, "claude");
        if (Directory.Exists(codexSessions))
            foreach (var file in Directory.EnumerateFiles(codexSessions, "*.jsonl", SearchOption.AllDirectories))
                yield return (file, "codex");
    }

    private sealed record CacheEntry(long Size, DateTime Modified, WorkSession? Session);

    private sealed record CacheFile(int Version, Dictionary<string, CacheEntry> Entries);

    private static readonly JsonSerializerOptions CacheJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Every session: read from the files that changed since the cache was written, from <paramref name="cachePath"/>
    /// for the rest. The cache is rewritten when anything was read. <paramref name="progress"/> gets (done, total).
    /// </summary>
    public static List<WorkSession> Load(string cachePath, IEnumerable<(string Path, string Agent)>? files = null,
        IProgress<(int Done, int Total)>? progress = null)
    {
        var all = (files ?? Files()).ToList();
        Dictionary<string, CacheEntry> entries = [];
        try
        {
            if (File.Exists(cachePath) && JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath), CacheJson) is { Version: CacheVersion } cache)
                entries = cache.Entries;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }

        var changed = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < all.Count; i++)
        {
            var (path, agent) = all[i];
            seen.Add(path);
            FileInfo info;
            try { info = new FileInfo(path); _ = info.Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (entries.TryGetValue(path, out var e) && e.Size == info.Length && e.Modified == info.LastWriteTimeUtc) continue;
            entries[path] = new CacheEntry(info.Length, info.LastWriteTimeUtc, agent == "codex" ? ReadCodex(path) : ReadClaude(path));
            changed = true;
            progress?.Report((i + 1, all.Count));
        }
        foreach (var gone in entries.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            entries.Remove(gone);
            changed = true;
        }
        if (changed)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllText(cachePath, JsonSerializer.Serialize(new CacheFile(CacheVersion, entries), CacheJson));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return entries.Values.Select(e => e.Session).OfType<WorkSession>().OrderByDescending(s => s.Start).ToList();
    }

    [GeneratedRegex(@"""id"":""(msg_[^""]+)""")] private static partial Regex IdOf();
    [GeneratedRegex(@"""model"":""([^""]+)""")] private static partial Regex ModelOf();
    [GeneratedRegex(@"<pasted_content[^>]*>[\s\S]*?</pasted_content>|<pasted_content[\s\S]*$")] private static partial Regex Pasted();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
