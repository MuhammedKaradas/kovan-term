using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>The link under a column of a terminal line: web addresses, plus <c>file:</c> and <c>mailto:</c>.</summary>
public static partial class TerminalLinks
{
    /// <summary>The link that covers <paramref name="column"/> in <paramref name="line"/>; <c>null</c> if there's none there.</summary>
    /// <param name="next">The following line: a link that runs to the last column goes on in it (wrapped).</param>
    /// <param name="width">The terminal's width in columns; 0 if unknown (no wrapping then).</param>
    public static string? At(string line, int column, string? next = null, int width = 0)
    {
        foreach (Match m in Link().Matches(line))
        {
            if (column < m.Index || column >= m.Index + m.Length) continue;
            var link = Trim(m.Value);
            // Wrapped: the link runs to the last column, and the next line starts with more of it
            if (next != null && width > 0 && m.Index + m.Length >= width
                && ContinuesLink().Match(next) is { Success: true, Length: > 0 } more)
                link = Trim(link + more.Value);
            return link;
        }
        return null;
    }

    /// <summary>A web address (http, https): opened in a preview when the setting says so; others go to the system.</summary>
    public static bool IsWeb(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // Punctuation that ends a sentence, or closes a bracket the link didn't open, isn't part of it
    private static string Trim(string link)
    {
        link = link.TrimEnd('.', ',', ';', ':', '!', '?', '\'', '"');
        if (link.EndsWith(')') && link.Count(c => c == '(') < link.Count(c => c == ')')) link = link[..^1];
        return link;
    }

    [GeneratedRegex(@"\b(?:https?://|file://|mailto:)[^\s<>""'`]+", RegexOptions.IgnoreCase)] private static partial Regex Link();
    [GeneratedRegex(@"^[^\s<>""'`]+")] private static partial Regex ContinuesLink();
}
