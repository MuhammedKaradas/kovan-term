using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace KovanTerm.Interop;

/// <summary>Where a search is: the match shown (1-based, counted from the oldest) and how many there are.</summary>
public sealed record SearchResult(int Index, int Count)
{
    public static SearchResult None { get; } = new(0, 0);

    public string Text => Count == 0 ? "No results" : Count >= TerminalSearch.MaxMatches ? $"{Index} of {Count}+" : $"{Index} of {Count}";
}

/// <summary>
/// Finds text in a terminal's buffer (#141), through the UI Automation text pattern the Windows Terminal control
/// provides (it has no search of its own for hosts): matches are found with <c>FindText</c>, the one shown is selected
/// in the terminal and scrolled into view. Call off the UI thread, like <see cref="TerminalScreenReader"/>.
/// </summary>
public sealed class TerminalSearch
{
    /// <summary>Counting stops here: a one-letter search in a long buffer would take a while.</summary>
    public const int MaxMatches = 500;

    private IntPtr _hwnd;
    private string _text = "";
    private int _index;

    /// <summary>
    /// Shows a match of <paramref name="text"/> (case-insensitive). A new search starts at the newest match (the bottom
    /// of the buffer, where the agent's latest output is); searching the same text again moves to the older match, or
    /// the newer one with <paramref name="newer"/>, wrapping around.
    /// </summary>
    public SearchResult Find(IntPtr hwnd, string text, bool newer = false)
    {
        if (text.Length == 0) return Reset();
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var p)) return Reset();
            var matches = Matches(((TextPattern)p).DocumentRange, text);
            if (matches.Count == 0)
            {
                Reset();
                return SearchResult.None;
            }
            var again = hwnd == _hwnd && string.Equals(text, _text, StringComparison.OrdinalIgnoreCase);
            _index = Step(again ? _index : 0, matches.Count, again, newer);
            (_hwnd, _text) = (hwnd, text);
            var match = matches[_index - 1];
            // The control's selection includes its end: without this, the character after the match is selected too
            var shown = match.Clone();
            if (text.Length > 1) shown.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -1);
            shown.Select();
            match.ScrollIntoView(alignToTop: false);
            return new SearchResult(_index, matches.Count);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ArgumentException
                                       or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine($"Couldn't search the terminal: {ex.Message}");
            return Reset();
        }
    }

    /// <summary>
    /// The match to show (1-based, oldest first): a new search shows the newest; the same search again moves one older,
    /// or newer, wrapping around. <paramref name="current"/> past the count (the buffer shrank) starts from the end.
    /// </summary>
    public static int Step(int current, int count, bool again, bool newer)
    {
        if (!again || current < 1 || current > count) return count;
        return newer ? current % count + 1 : (current + count - 2) % count + 1;
    }

    private SearchResult Reset()
    {
        (_hwnd, _text, _index) = (IntPtr.Zero, "", 0);
        return SearchResult.None;
    }

    private static List<TextPatternRange> Matches(TextPatternRange document, string text)
    {
        var list = new List<TextPatternRange>();
        var rest = document.Clone();
        while (list.Count < MaxMatches && rest.FindText(text, backward: false, ignoreCase: true) is { } match)
        {
            list.Add(match);
            // Search on from the end of this match
            rest.MoveEndpointByRange(TextPatternRangeEndpoint.Start, match, TextPatternRangeEndpoint.End);
            if (rest.CompareEndpoints(TextPatternRangeEndpoint.Start, rest, TextPatternRangeEndpoint.End) >= 0) break;
        }
        return list;
    }
}
