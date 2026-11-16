namespace KovanTerm.Services;

public enum DiffLineKind
{
    /// <summary>File header lines (<c>diff --git</c>, <c>index</c>, <c>---</c>, <c>+++</c>); not shown.</summary>
    Header,
    Hunk,
    Context,
    Added,
    Removed,
    /// <summary>Anything else, e.g. "Binary files differ", "\ No newline at end of file".</summary>
    Note,
}

/// <summary>One line of a unified diff, with its line numbers in the old and new file.</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldLine, int? NewLine)
{
    /// <summary>Line number to refer to in a note: the new file's, or the old one's for a removed line.</summary>
    public int? Line => NewLine ?? OldLine;
    public string Marker => Kind switch { DiffLineKind.Added => "+", DiffLineKind.Removed => "-", _ => " " };
    public bool CanAnnotate => Kind is DiffLineKind.Context or DiffLineKind.Added or DiffLineKind.Removed;
}

/// <summary>Parses <c>git diff</c> output (unified format) into lines with line numbers.</summary>
public static class DiffParser
{
    public static IReadOnlyList<DiffLine> Parse(string diff)
    {
        var lines = new List<DiffLine>();
        int oldLine = 0, newLine = 0;
        var inHunk = false;
        foreach (var raw in diff.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = TryParseHunk(raw, out oldLine, out newLine);
                lines.Add(new DiffLine(DiffLineKind.Hunk, raw, null, null));
                continue;
            }
            if (!inHunk)
            {
                if (raw.Length > 0) lines.Add(new DiffLine(IsHeader(raw) ? DiffLineKind.Header : DiffLineKind.Note, raw, null, null));
                continue;
            }
            switch (raw.FirstOrDefault())
            {
                case '+': lines.Add(new DiffLine(DiffLineKind.Added, raw[1..], null, newLine++)); break;
                case '-': lines.Add(new DiffLine(DiffLineKind.Removed, raw[1..], oldLine++, null)); break;
                case ' ': lines.Add(new DiffLine(DiffLineKind.Context, raw[1..], oldLine++, newLine++)); break;
                case '\\': lines.Add(new DiffLine(DiffLineKind.Note, raw, null, null)); break;
                default:
                    // An empty string is the end of the output; anything else starts the next file's header
                    if (raw.Length > 0)
                    {
                        inHunk = false;
                        lines.Add(new DiffLine(IsHeader(raw) ? DiffLineKind.Header : DiffLineKind.Note, raw, null, null));
                    }
                    break;
            }
        }
        return lines;
    }

    private static bool IsHeader(string line) =>
        line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal) ||
        line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("+++ ", StringComparison.Ordinal) ||
        line.StartsWith("new file mode", StringComparison.Ordinal) || line.StartsWith("deleted file mode", StringComparison.Ordinal) ||
        line.StartsWith("old mode", StringComparison.Ordinal) || line.StartsWith("new mode", StringComparison.Ordinal) ||
        line.StartsWith("similarity index", StringComparison.Ordinal) || line.StartsWith("rename ", StringComparison.Ordinal);

    // "@@ -12,5 +12,7 @@ optional section"
    private static bool TryParseHunk(string line, out int oldStart, out int newStart)
    {
        oldStart = newStart = 0;
        var parts = line.Split(' ');
        return parts.Length >= 3 && ParseStart(parts[1], '-', out oldStart) && ParseStart(parts[2], '+', out newStart);

        static bool ParseStart(string range, char sign, out int start)
        {
            start = 0;
            if (range.Length < 2 || range[0] != sign) return false;
            var comma = range.IndexOf(',');
            return int.TryParse(comma > 0 ? range[1..comma] : range[1..], out start);
        }
    }
}
