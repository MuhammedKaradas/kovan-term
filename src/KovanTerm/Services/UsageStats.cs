using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace KovanTerm.Services;

/// <summary>
/// Usage statistics, sent only when the user agreed and only by builds given an address
/// (<c>-p:KovanUsageEndpoint=https://…</c>): how many times each event in <see cref="Events"/> happened, per day.
/// Nothing else can be counted: an unknown name is ignored, so no file name, path, prompt or text can slip in.
/// Counts are kept in <c>usage.json</c>; finished days are sent at the next start. The format is in
/// <c>docs/usage-stats.md</c>.
/// </summary>
public static class UsageStats
{
    public const int Schema = 1;
    public const int MaxDays = 30;

    /// <summary>Every event that can be counted, with what it means (the docs list the same).</summary>
    public static IReadOnlyDictionary<string, string> Events { get; } = new Dictionary<string, string>
    {
        ["app.start"] = "Kovan was opened",
        ["node.shell"] = "A shell terminal (PowerShell, cmd, WSL…) was opened",
        ["node.claude"] = "A Claude Code node was opened (including DeepSeek and Ollama profiles)",
        ["node.codex"] = "A Codex node was opened",
        ["node.antigravity"] = "An Antigravity node was opened",
        ["node.gemini"] = "A Gemini CLI node was opened",
        ["node.cursor"] = "A Cursor CLI node was opened",
        ["node.opencode"] = "An OpenCode node was opened",
        ["node.qwen"] = "A Qwen Code node was opened",
        ["node.other"] = "A node with another program was opened",
        ["node.note"] = "A note was added",
        ["node.preview"] = "A web preview was added",
        ["node.group"] = "A group was made",
        ["profile.deepseek"] = "The Claude Code (DeepSeek) profile was used",
        ["profile.ollama"] = "The Claude Code (Ollama) profile was used",
        ["profile.custom"] = "A profile from profiles.json was used",
        ["feature.worktree"] = "A node was started in a new git worktree",
        ["feature.broadcast"] = "A prompt was sent to several agents",
        ["feature.recent"] = "A conversation was reopened from Recent",
        ["feature.find"] = "Text was searched for in a terminal",
        ["feature.conflicts"] = "A pull request's conflicts were sent to the agent",
        ["feature.viewed"] = "A changed file was marked as viewed",
        ["feature.bypass"] = "A folder was set to start Claude Code without permission prompts",
        ["feature.clear"] = "A terminal's scrollback was cleared",
        ["feature.outside"] = "Showing sessions started outside Kovan was turned on",
        ["feature.outside.open"] = "A session started outside Kovan was opened in Kovan",
        ["feature.allow"] = "Allow or Deny was pressed in Kovan",
        ["feature.dictation"] = "Dictation was used",
        ["feature.voice"] = "A voice command was used",
        ["feature.statistics"] = "The Statistics window was opened",
        ["feature.board"] = "A task was added to the Board",
        ["feature.pull_request"] = "Create pull request or Merge was chosen on a worktree node",
        ["feature.workspace"] = "A workspace was added",
    };

    public static string? Endpoint { get; } = BuildSettings.Endpoint("UsageEndpoint");

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "usage.json");

    /// <summary>Set at startup and when the setting changes.</summary>
    public static volatile bool Enabled;
    private static string? _installId;

    public static void Apply(Models.AppSettings settings)
    {
        _installId = settings.InstallId;
        Enabled = Endpoint != null && settings.SendUsageStats == true && settings.InstallId != null;
    }

    /// <summary>The settings with statistics turned on or off; turning them on makes the install id if there's none.</summary>
    public static Models.AppSettings With(Models.AppSettings settings, bool send) => settings with
    {
        SendUsageStats = send,
        InstallId = send ? settings.InstallId ?? Guid.NewGuid().ToString() : settings.InstallId,
    };

    // The file is small and events are rare: it's read and written each time, so nothing is lost to a crash
    private static readonly object Lock = new();

    /// <summary>Counts <paramref name="name"/> for today if the user agreed; names not in <see cref="Events"/> are ignored.</summary>
    public static void Count(string name)
    {
        if (!Enabled || !Events.ContainsKey(name)) return;
        lock (Lock)
        {
            var days = Load(FilePath);
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            if (!days.TryGetValue(day, out var counts)) days[day] = counts = [];
            counts[name] = counts.GetValueOrDefault(name) + 1;
            Save(FilePath, days);
        }
    }

    /// <summary>The event for a newly opened node.</summary>
    public static string NodeEvent(string? agentKind, bool isNote, bool isPreview, bool isGroup, string? profileId) =>
        isNote ? "node.note" : isPreview ? "node.preview" : isGroup ? "node.group"
        : agentKind is { } k && Events.ContainsKey("node." + k) ? "node." + k
        : profileId is "powershell" or "pwsh" or "cmd" || profileId?.StartsWith("wsl-", StringComparison.Ordinal) == true ? "node.shell"
        : "node.other";

    /// <summary>The event for a profile picked in the New terminal dialog, if it's one worth counting.</summary>
    public static string? ProfileEvent(string profileId, bool builtIn) => profileId switch
    {
        "claude-deepseek" => "profile.deepseek",
        "claude-ollama" => "profile.ollama",
        _ => builtIn ? null : "profile.custom",
    };

    public static Dictionary<string, Dictionary<string, int>> Load(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(path)) is { } days)
                return days;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return [];
    }

    private static void Save(string path, Dictionary<string, Dictionary<string, int>> days)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(days));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The request body for the finished days (before <paramref name="today"/>), oldest first; <c>null</c> if none.</summary>
    public static string? Body(Dictionary<string, Dictionary<string, int>> days, string installId, DateOnly today)
    {
        var finished = days.Where(d => DateOnly.TryParse(d.Key, out var date) && date < today)
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d => new
            {
                date = d.Key,
                counts = d.Value.Where(c => Events.ContainsKey(c.Key) && c.Value > 0).OrderBy(c => c.Key, StringComparer.Ordinal)
                    .ToDictionary(c => c.Key, c => c.Value),
            })
            .Where(d => d.counts.Count > 0)
            .ToList();
        if (finished.Count == 0) return null;
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            installId,
            appVersion = UpdateCheck.CurrentVersion,
            os = $"Windows {Environment.OSVersion.Version}",
            days = finished,
        });
    }

    /// <summary>
    /// Sends the finished days in one request and forgets them once the server took them (or refused them with a 4xx);
    /// drops days older than <see cref="MaxDays"/>. Returns whether anything was sent.
    /// </summary>
    public static async Task<bool> SendAsync(HttpClient http, string endpoint, string path, string installId, DateOnly today)
    {
        string? body;
        lock (Lock)
        {
            var days = Load(path);
            var old = days.Keys.Where(k => !DateOnly.TryParse(k, out var d) || today.DayNumber - d.DayNumber > MaxDays).ToList();
            foreach (var day in old) days.Remove(day);
            if (old.Count > 0) Save(path, days);
            body = Body(days, installId, today);
        }
        if (body == null) return false;
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
        using (response)
        {
            var refused = (int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.TooManyRequests;
            if (!response.IsSuccessStatusCode && !refused) return false;
            // Read again: today's counts may have grown while the request was out
            lock (Lock)
            {
                var days = Load(path);
                foreach (var sent in days.Keys.Where(k => DateOnly.TryParse(k, out var d) && d < today).ToList()) days.Remove(sent);
                Save(path, days);
            }
            return response.IsSuccessStatusCode;
        }
    }

    /// <summary>Sends the finished days, if the user agreed; failures wait for the next start.</summary>
    public static Task SendAsync() =>
        !Enabled || Endpoint is not { } endpoint || _installId is not { } id ? Task.CompletedTask
        : Task.Run(async () =>
        {
            try { await SendAsync(BuildSettings.Http, endpoint, FilePath, id, DateOnly.FromDateTime(DateTime.Now)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException) { }
        });
}

/// <summary>Addresses given to a build (<c>-p:Kovan…Endpoint=</c>), and the HTTP client that sends to them.</summary>
public static class BuildSettings
{
    /// <summary>An HTTPS address from the assembly's metadata; <c>null</c> if the build wasn't given one.</summary>
    public static string? Endpoint(string key)
    {
        var value = typeof(BuildSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    }

    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"KovanTerm/{UpdateCheck.CurrentVersion}");
        return http;
    });

    public static HttpClient Http => SharedHttp.Value;
}
