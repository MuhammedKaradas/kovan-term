using System.Text;

namespace KovanTerm.Services;

/// <summary>One OSC 7501 (Program Status Protocol) report.</summary>
/// <param name="State">idle, working, done, blocked, error or clear.</param>
/// <param name="Kind">For blocked: permission, question or auth.</param>
/// <param name="Id">Record id; <c>null</c> for the root record (the program itself), otherwise e.g. a subagent.</param>
/// <param name="Message">Decoded, sanitized <c>msg</c>.</param>
/// <param name="App">Program name, e.g. <c>codex</c>.</param>
public sealed record ProgramStatusReport(string State, string? Kind = null, string? Id = null, string? Message = null, string? App = null);

/// <summary>
/// Finds OSC 7501 reports (<c>ESC ] 7501 ; state=working:app=codex BEL</c>) in a terminal's output stream.
/// Programs that emit them without waiting for the terminal's answer to the <c>OSC 7501 ; ?</c> probe (Codex)
/// report their status this way. One instance per terminal: a report split across output chunks is
/// completed with the next chunk. See decision 0003 for why Claude Code doesn't report this way under ConPTY.
/// </summary>
public sealed class ProgramStatusParser
{
    private const string Prefix = "\x1b]7501;";
    // The spec caps a report at 4096 bytes; anything longer isn't a report
    private const int MaxReportLength = 4096;
    private const int MaxMessageLength = 200;

    private string _carry = "";

    /// <summary>
    /// Reports completed by this chunk, in order. Called for every output chunk, so the common case (no report)
    /// is one vectorized search and allocates nothing.
    /// </summary>
    public IReadOnlyList<ProgramStatusReport> Feed(string chunk)
    {
        if (_carry.Length == 0 && !chunk.Contains(Prefix, StringComparison.Ordinal) && PartialPrefixAtEnd(chunk).Length == 0)
            return [];
        List<ProgramStatusReport>? result = null;

        var text = _carry + chunk;
        _carry = "";
        var pos = 0;
        while (true)
        {
            var start = text.IndexOf(Prefix, pos, StringComparison.Ordinal);
            if (start < 0)
            {
                // The prefix itself may be split: keep a trailing partial prefix
                _carry = PartialPrefixAtEnd(text);
                break;
            }
            var bodyStart = start + Prefix.Length;
            var end = FindTerminator(text, bodyStart, out var terminatorLength);
            if (end == -2)
            {
                pos = bodyStart;
                continue;
            }
            if (end < 0)
            {
                if (text.Length - start <= MaxReportLength) _carry = text[start..];
                break;
            }
            if (end - bodyStart <= MaxReportLength && Parse(text.AsSpan(bodyStart, end - bodyStart)) is { } report)
                (result ??= []).Add(report);
            pos = end + terminatorLength;
        }
        return result ?? (IReadOnlyList<ProgramStatusReport>)[];
    }

    private static int FindTerminator(string text, int from, out int length)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] == '\x07') { length = 1; return i; }
            if (text[i] == '\x1b')
            {
                // ESC \ ends it; a lone ESC at the end may be the first half of one
                if (i + 1 == text.Length) { length = 0; return -1; }
                if (text[i + 1] == '\\') { length = 2; return i; }
                length = 0;
                return -2; // malformed: another sequence started; drop this report
            }
        }
        length = 0;
        return -1;
    }

    private static string PartialPrefixAtEnd(string text)
    {
        for (var n = Math.Min(Prefix.Length - 1, text.Length); n > 0; n--)
            if (text.AsSpan(text.Length - n).SequenceEqual(Prefix.AsSpan(0, n))) return text[^n..];
        return "";
    }

    /// <summary>Parses the body (<c>key=value</c> pairs separated by <c>:</c>); <c>null</c> without a valid state.</summary>
    public static ProgramStatusReport? Parse(ReadOnlySpan<char> body)
    {
        if (body.StartsWith("?")) return null; // a query, not a report
        string? state = null, kind = null, id = null, msg = null, app = null;
        foreach (var range in body.Split(':'))
        {
            var pair = body[range];
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var value = pair[(eq + 1)..].ToString();
            switch (pair[..eq])
            {
                case "state": state = value; break;
                case "kind": kind = value; break;
                case "id": id = value.Length == 0 ? null : value; break;
                case "msg": msg = DecodeText(value); break;
                case "app": app = value; break;
            }
        }
        return state is "idle" or "working" or "done" or "blocked" or "error" or "clear"
            ? new ProgramStatusReport(state, kind, id, msg, app)
            : null;
    }

    /// <summary>Base64 UTF-8 text, made safe to show: no control or direction-override characters, length capped.</summary>
    private static string? DecodeText(string base64)
    {
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var safe = new string(text.Where(c => !char.IsControl(c) && c is not (>= '‪' and <= '‮') and not (>= '⁦' and <= '⁩')).ToArray()).Trim();
            if (safe.Length == 0) return null;
            return safe.Length > MaxMessageLength ? safe[..MaxMessageLength] + "…" : safe;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
