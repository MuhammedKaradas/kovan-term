using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace KovanTerm.Services;

/// <summary>A published release newer than the running app.</summary>
public sealed record AvailableUpdate(string Version, string Url);

/// <summary>
/// Asks GitHub for the latest published release (drafts and pre-releases aren't "latest") and compares it with the
/// running version. Nothing is downloaded or installed: the app only says a newer one exists. Turned off in Settings.
/// </summary>
public static class UpdateCheck
{
    public const string Repository = "MuhammedKaradas/kovan-term";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub's API refuses requests without a User-Agent; only the app's name and version are sent
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KovanTerm", CurrentVersion));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>The running version, e.g. <c>0.2.0</c> (without the <c>+commit</c> a source build carries).</summary>
    public static string CurrentVersion { get; } = Strip(
        typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(UpdateCheck).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    private static string Strip(string version)
    {
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    /// <summary>The latest release if it's newer than <paramref name="current"/>; <c>null</c> if not, or on any failure.</summary>
    public static async Task<AvailableUpdate?> CheckAsync(string? current = null)
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest").ConfigureAwait(false);
            // No release published yet, rate limited, offline…: nothing to say
            if (!response.IsSuccessStatusCode) return null;
            return Newer(await response.Content.ReadAsStringAsync().ConfigureAwait(false), current ?? CurrentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The release in a <c>releases/latest</c> answer, if it's newer than <paramref name="current"/>.</summary>
    public static AvailableUpdate? Newer(string json, string current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } tagName)
            return null;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        var version = tagName.TrimStart('v', 'V');
        if (Compare(version, current) <= 0) return null;
        var url = root.TryGetProperty("html_url", out var u) && u.GetString() is { Length: > 0 } page
            && page.StartsWith($"https://github.com/{Repository}/", StringComparison.OrdinalIgnoreCase)
            ? page : ReleasesPage;
        return new AvailableUpdate(version, url);
    }

    /// <summary>
    /// Compares two SemVer versions (major.minor.patch[-pre]); a pre-release sorts before its release. Unreadable
    /// parts count as 0.
    /// </summary>
    public static int Compare(string a, string b)
    {
        var (coreA, preA) = Split(Strip(a));
        var (coreB, preB) = Split(Strip(b));
        for (var i = 0; i < 3; i++)
        {
            var c = coreA[i].CompareTo(coreB[i]);
            if (c != 0) return c;
        }
        if (preA == preB) return 0;
        if (preA == null) return 1;
        if (preB == null) return -1;
        return string.CompareOrdinal(preA, preB);
    }

    private static (int[] Core, string? Pre) Split(string version)
    {
        var dash = version.IndexOf('-');
        var pre = dash >= 0 ? version[(dash + 1)..] : null;
        var parts = (dash >= 0 ? version[..dash] : version).Split('.');
        var core = new int[3];
        for (var i = 0; i < 3 && i < parts.Length; i++) core[i] = int.TryParse(parts[i], out var n) ? n : 0;
        return (core, pre);
    }
}
