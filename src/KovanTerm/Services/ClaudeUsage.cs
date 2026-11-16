using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KovanTerm.Services;

/// <summary>One of the Claude plan's usage limits, e.g. the 5-hour session or the weekly limit.</summary>
/// <param name="Label">"5h", "Week", "Week · Fable"…</param>
/// <param name="Percent">How much of it is used, 0–100.</param>
/// <param name="Severity">The service's own judgement: <c>normal</c>, <c>warning</c>…</param>
public sealed record UsageLimit(string Label, double Percent, DateTimeOffset? ResetsAt, string? Severity)
{
    public bool IsHigh => Percent >= 80 || Severity is "warning" or "critical" or "exceeded";

    /// <summary>"in 2 h 10 m", "in 3 d", or <c>null</c> when unknown.</summary>
    public string? ResetsIn(DateTimeOffset now) => ResetsAt is not { } at ? null : (at - now) switch
    {
        var d when d <= TimeSpan.Zero => "now",
        var d when d.TotalHours < 1 => $"in {Math.Max(1, (int)d.TotalMinutes)} m",
        var d when d.TotalDays < 1 => $"in {(int)d.TotalHours} h {d.Minutes} m",
        var d => $"in {(int)Math.Round(d.TotalDays)} d",
    };
}

/// <summary>
/// The Claude plan's usage limits, as Claude Code's <c>/usage</c> shows them. Uses Claude Code's own login
/// (<c>~/.claude/.credentials.json</c>) and sends its token only to Anthropic's API that issued it. The endpoint isn't
/// documented, so anything unexpected — no login, an expired token, a changed response — gives "no data" and the
/// meter is hidden. The token is never refreshed here: that would rewrite Claude Code's credentials.
/// </summary>
public sealed class ClaudeUsage(HttpClient http, string? credentialsFile = null)
{
    private const string Url = "https://api.anthropic.com/api/oauth/usage";
    private readonly string _credentialsFile = credentialsFile ?? Path.Combine(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
        ".credentials.json");

    /// <summary>The current limits; empty when they can't be known.</summary>
    public async Task<IReadOnlyList<UsageLimit>> ReadAsync(CancellationToken ct = default)
    {
        if (ReadToken(_credentialsFile, DateTimeOffset.UtcNow) is not { } token) return [];
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("KovanTerm");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return [];
            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Debug.WriteLine($"Couldn't read Claude usage: {ex.Message}");
            return [];
        }
    }

    /// <summary>The OAuth access token, if Claude Code is logged in and it hasn't expired.</summary>
    public static string? ReadToken(string credentialsFile, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(credentialsFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(credentialsFile));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return null;
            if (oauth.TryGetProperty("expiresAt", out var exp) && exp.TryGetInt64(out var ms)
                && DateTimeOffset.FromUnixTimeMilliseconds(ms) <= now) return null;
            return oauth.TryGetProperty("accessToken", out var t) && t.GetString() is { Length: > 0 } token ? token : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the response's <c>limits</c> list; unknown kinds keep the service's own name.</summary>
    public static IReadOnlyList<UsageLimit> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array) return [];
        var result = new List<UsageLimit>();
        foreach (var l in limits.EnumerateArray())
        {
            if (!l.TryGetProperty("percent", out var p) || !p.TryGetDouble(out var percent)) continue;
            var kind = Str(l, "kind") ?? "limit";
            var model = l.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object
                && scope.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.Object ? Str(m, "display_name") : null;
            var label = kind switch
            {
                "session" => "5h",
                "weekly_all" => "Week",
                "weekly_scoped" => model != null ? $"Week · {model}" : "Week (scoped)",
                _ => kind.Replace('_', ' '),
            };
            DateTimeOffset? resets = Str(l, "resets_at") is { } r && DateTimeOffset.TryParse(r, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            result.Add(new UsageLimit(label, Math.Clamp(percent, 0, 100), resets, Str(l, "severity")));
        }
        return result;
    }

    private static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
