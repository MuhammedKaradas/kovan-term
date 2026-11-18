using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>
/// Finds local web addresses in terminal output, like a dev server's "Local: http://localhost:5173/", so the
/// terminal can offer a preview of it. Only loopback addresses: nothing on the internet is opened by itself.
/// </summary>
public static partial class LocalUrls
{
    // The address may be wrapped in color codes; they end the match (\x1b isn't allowed in it)
    // The host must end there (a port, a path or the end): "http://localhostile.example.com" isn't local
    [GeneratedRegex(@"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1\])(?![\w.-])(?::\d{1,5})?(?:/[^\s""'<>\x1b`]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>The last local address in the text, cleaned up; <c>null</c> if there's none. Cheap when there's no "://".</summary>
    public static string? FindLast(string text)
    {
        if (!text.Contains("://", StringComparison.Ordinal)) return null;
        string? last = null;
        foreach (Match m in Pattern().Matches(text)) last = m.Value;
        if (last == null) return null;
        // Trailing punctuation belongs to the sentence, not the address; 0.0.0.0 means "all interfaces": open it locally
        last = last.TrimEnd('.', ',', ')', ']', ';', ':', '!', '?');
        return Regex.Replace(last, "//0\\.0\\.0\\.0", "//localhost", RegexOptions.IgnoreCase);
    }

    /// <summary>What the address bar's text means: adds <c>http://</c> when missing; <c>null</c> if it isn't a web address.</summary>
    public static Uri? Normalize(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "http://" + t;
        return Uri.TryCreate(t, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri : null;
    }
}
