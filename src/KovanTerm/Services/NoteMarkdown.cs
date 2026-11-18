using System.Text.RegularExpressions;

namespace KovanTerm.Services;

public enum NoteBlockKind
{
    Paragraph,
    Heading,
    Bullet,
    Numbered,
    Task,
    Quote,
    Code,
    Rule,
    Blank,
}

/// <summary>A line (or a fenced code block) of a note. <see cref="Line"/> is its first line in the note's text.</summary>
public sealed record NoteBlock(NoteBlockKind Kind, string Text, int Line, int Level = 0, bool Checked = false, string? Number = null);

public enum NoteSpanKind
{
    Text,
    Bold,
    Italic,
    Code,
    Link,
}

public sealed record NoteSpan(NoteSpanKind Kind, string Text, string? Url = null);

/// <summary>
/// The small part of Markdown that notes use: headings, lists, task lists, quotes, fenced code, rules and
/// **bold**, *italic*, `code` and [links](url) inside a line. Line based, so a task's checkbox can be toggled in the text.
/// </summary>
public static partial class NoteMarkdown
{
    public static IReadOnlyList<NoteBlock> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<NoteBlock>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var indent = (line.Length - trimmed.Length) / 2;

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var start = i;
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                blocks.Add(new NoteBlock(NoteBlockKind.Code, string.Join('\n', code), start));
                continue;
            }
            if (trimmed.Length == 0) { blocks.Add(new NoteBlock(NoteBlockKind.Blank, "", i)); continue; }
            if (RuleLine().IsMatch(trimmed)) { blocks.Add(new NoteBlock(NoteBlockKind.Rule, "", i)); continue; }
            if (HeadingLine().Match(trimmed) is { Success: true } h)
            {
                blocks.Add(new NoteBlock(NoteBlockKind.Heading, h.Groups[2].Value.Trim(), i, h.Groups[1].Length));
                continue;
            }
            if (TaskLine().Match(trimmed) is { Success: true } t)
            {
                blocks.Add(new NoteBlock(NoteBlockKind.Task, t.Groups[2].Value, i, indent, Checked: t.Groups[1].Value is "x" or "X"));
                continue;
            }
            if (BulletLine().Match(trimmed) is { Success: true } b)
            {
                blocks.Add(new NoteBlock(NoteBlockKind.Bullet, b.Groups[1].Value, i, indent));
                continue;
            }
            if (NumberedLine().Match(trimmed) is { Success: true } n)
            {
                blocks.Add(new NoteBlock(NoteBlockKind.Numbered, n.Groups[2].Value, i, indent, Number: n.Groups[1].Value));
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                blocks.Add(new NoteBlock(NoteBlockKind.Quote, trimmed[1..].TrimStart(), i));
                continue;
            }
            blocks.Add(new NoteBlock(NoteBlockKind.Paragraph, trimmed, i));
        }
        return blocks;
    }

    /// <summary>Ticks or unticks the task on <paramref name="line"/>; the text is unchanged if that line isn't a task.</summary>
    public static string ToggleTask(string text, int line)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (line < 0 || line >= lines.Length) return text;
        var m = TaskBox().Match(lines[line]);
        if (!m.Success) return text;
        var box = m.Groups[1];
        lines[line] = lines[line][..box.Index] + (box.Value == " " ? "x" : " ") + lines[line][(box.Index + 1)..];
        return string.Join(newline, lines);
    }

    /// <summary>Splits a line into plain, bold, italic, code and link spans.</summary>
    public static IReadOnlyList<NoteSpan> Inline(string text)
    {
        var spans = new List<NoteSpan>();
        var last = 0;
        foreach (Match m in InlineMarkup().Matches(text))
        {
            if (m.Index > last) spans.Add(new NoteSpan(NoteSpanKind.Text, text[last..m.Index]));
            if (m.Groups["code"].Success) spans.Add(new NoteSpan(NoteSpanKind.Code, m.Groups["code"].Value));
            else if (m.Groups["bold"].Success) spans.Add(new NoteSpan(NoteSpanKind.Bold, m.Groups["bold"].Value));
            else if (m.Groups["italic"].Success) spans.Add(new NoteSpan(NoteSpanKind.Italic, m.Groups["italic"].Value));
            else spans.Add(new NoteSpan(NoteSpanKind.Link, m.Groups["label"].Value, m.Groups["url"].Value));
            last = m.Index + m.Length;
        }
        if (last < text.Length) spans.Add(new NoteSpan(NoteSpanKind.Text, text[last..]));
        return spans;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^[-*+]\s+\[([ xX])\]\s?(.*)$")]
    private static partial Regex TaskLine();

    [GeneratedRegex(@"^\s*[-*+]\s+\[([ xX])\]")]
    private static partial Regex TaskBox();

    [GeneratedRegex(@"^[-*+]\s+(.*)$")]
    private static partial Regex BulletLine();

    [GeneratedRegex(@"^(\d{1,9})[.)]\s+(.*)$")]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$")]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>[^*]+)\*\*|__(?<bold>[^_]+)__|(?<![\w*])\*(?<italic>[^*\s][^*]*)\*(?![\w*])|(?<!\w)_(?<italic>[^_\s][^_]*)_(?!\w)|\[(?<label>[^\]]+)\]\((?<url>https?://[^)\s]+)\)")]
    private static partial Regex InlineMarkup();
}
