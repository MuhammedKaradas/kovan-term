using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KovanTerm.Services.Voice;

/// <summary>What a voice command asks for (#21).</summary>
public enum VoiceCommandKind
{
    /// <summary>Not understood.</summary>
    Unknown,
    GoToWaiting,
    GoTo,
    /// <summary>Type <see cref="VoiceCommand.Text"/> into an agent and send it.</summary>
    Tell,
    Allow,
    Deny,
    Close,
    Zoom,
    FitAll,
    NewTerminal,
    NewNote,
    NextWorkspace,
    PreviousWorkspace,
    SwitchWorkspace,
    ToggleBoard,
    ToggleChanges,
    ToggleAgents,
}

/// <summary>Something a command can name: a node or a workspace.</summary>
public sealed record VoiceTarget(object Key, string Name);

/// <param name="Targets">What the name heard matched (several if it's ambiguous); empty if no name was said.</param>
/// <param name="NameHeard">The name as heard, if one was said (also when nothing matched it).</param>
/// <param name="Text">What to tell an agent, or a new note's text.</param>
public sealed record VoiceCommand(VoiceCommandKind Kind, IReadOnlyList<object> Targets, string? NameHeard = null, string? Text = null)
{
    public static VoiceCommand Of(VoiceCommandKind kind) => new(kind, []);

    /// <summary>A name was said but nothing is called that.</summary>
    public bool NameNotFound => NameHeard != null && Targets.Count == 0;
}

/// <summary>
/// Turns a dictated sentence into a <see cref="VoiceCommand"/> with fixed Turkish and English patterns, on this
/// computer (decision record 0004). Names are matched loosely: Whisper may write "Claude'a", "Claudea" or "Klod".
/// </summary>
public static partial class VoiceCommands
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Example commands, for the status line when one isn't understood.</summary>
    public const string Examples = "\"go to the waiting agent\", \"tell Claude to run the tests\", \"bekleyen ajana git\", \"Claude'a söyle testleri çalıştır\"";

    /// <summary>
    /// What Whisper is told to expect: sample commands that use the names on the canvas. Names alone make it write the
    /// Turkish words around them as English ("Hepsini göster" came out "Hepsi ni güzler"), and a list of names is
    /// sometimes written back when the recording is short.
    /// </summary>
    public static string Prompt(IEnumerable<string> names)
    {
        var list = names.Select(n => Regex.Replace(n, @"\s*\(.*\)\s*$", "").Trim()).Where(n => n.Length > 0).Distinct().Take(6).ToList();
        if (list.Count == 0) list.Add("Claude");
        string N(int i) => list[i % list.Count];
        return $"Bekleyen ajana git. {N(0)}'a git. Hepsini göster. {N(1)}'e söyle testleri çalıştır. Not al. " +
               $"{N(2)}'i kapat. İzin ver. Tell {N(3)} to run the tests. Go to {N(4)}.";
    }

    public static VoiceCommand Parse(string heard, IReadOnlyList<VoiceTarget> nodes, IReadOnlyList<VoiceTarget> workspaces)
    {
        var original = heard.Trim().TrimEnd('.', '!', '?', ',', ';', ':').Trim();
        var f = Fold(original);
        Match m;

        // Answers to a permission prompt; "izin verme" before "izin ver"
        if ((m = DenyTr().Match(f)).Success || (m = DenyEn().Match(f)).Success)
            return Answer(VoiceCommandKind.Deny, m, nodes);
        if ((m = AllowTr().Match(f)).Success || (m = AllowEn().Match(f)).Success)
            return Answer(VoiceCommandKind.Allow, m, nodes);

        if (Waiting().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.GoToWaiting);
        if (FitAll().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.FitAll);

        if (PanelVerb().IsMatch(f))
        {
            if (Board().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.ToggleBoard);
            if (Changes().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.ToggleChanges);
            if (AgentsPanel().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.ToggleAgents);
        }

        if (NextWorkspace().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.NextWorkspace);
        if (PreviousWorkspace().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.PreviousWorkspace);
        if ((m = SwitchWorkspaceTr().Match(f)).Success || (m = SwitchWorkspaceEn().Match(f)).Success)
        {
            var command = Named(VoiceCommandKind.SwitchWorkspace, m, workspaces);
            // "workspace 2" is heard as "2"
            return command.NameNotFound && Match("workspace " + command.NameHeard, workspaces) is { Count: > 0 } numbered
                ? command with { Targets = numbered }
                : command;
        }

        if ((m = NewNote().Match(f)).Success)
            return new VoiceCommand(VoiceCommandKind.NewNote, [], Text: Span(original, m.Groups["t"]));
        if (NewTerminal().IsMatch(f)) return VoiceCommand.Of(VoiceCommandKind.NewTerminal);

        if ((m = ZoomTr().Match(f)).Success || (m = ZoomEn().Match(f)).Success)
            return Named(VoiceCommandKind.Zoom, m, nodes);

        if (ParseTell(original, f, nodes) is { } tell) return tell;

        if ((m = CloseTr().Match(f)).Success || (m = CloseEn().Match(f)).Success)
            return Named(VoiceCommandKind.Close, m, nodes);
        if ((m = GoToTr().Match(f)).Success || (m = GoToEn().Match(f)).Success)
            return Named(VoiceCommandKind.GoTo, m, nodes);

        return VoiceCommand.Of(VoiceCommandKind.Unknown);
    }

    private static VoiceCommand? ParseTell(string original, string f, IReadOnlyList<VoiceTarget> nodes)
    {
        // "Claude'a söyle testleri çalıştır", "Claude'a söyle: ..."
        if (TellTrNameFirst().Match(f) is { Success: true } a && Match(a.Groups["n"].Value, nodes) is { Count: > 0 } first)
            return Tell(first, a.Groups["n"].Value, Imperative(Span(original, a.Groups["t"])));
        // "Claude'a testleri çalıştırmasını söyle"
        if (TellTrNameFirstVerbLast().Match(f) is { Success: true } d && Match(d.Groups["n"].Value, nodes) is { Count: > 0 } named)
            return Tell(named, d.Groups["n"].Value, Imperative(Span(original, d.Groups["t"])));
        // "testleri çalıştırmasını Claude'a söyle"
        if (TellTrNameLast().Match(f) is { Success: true } b && Match(b.Groups["n"].Value, nodes) is { Count: > 0 } last)
            return Tell(last, b.Groups["n"].Value, Imperative(Span(original, b.Groups["t"])));
        // "tell Claude to run the tests", "ask the reviewer to look at it": the name is the shortest run of words that matches
        if (TellEn().Match(f) is { Success: true } c)
        {
            var rest = c.Groups["rest"];
            var words = WordSpans().Matches(rest.Value).ToList();
            for (var n = 1; n <= Math.Min(3, words.Count - 1); n++)
            {
                var name = rest.Value[..(words[n - 1].Index + words[n - 1].Length)];
                if (Match(name, nodes) is not { Count: > 0 } targets) continue;
                var start = rest.Index + words[n].Index;
                var text = original[start..];
                text = Regex.Replace(text, @"^(?:to|that)\s+", "", RegexOptions.IgnoreCase);
                return Tell(targets, name, text);
            }
        }
        return null;
    }

    private static VoiceCommand Tell(IReadOnlyList<object> targets, string name, string text) =>
        new(VoiceCommandKind.Tell, targets, name.Trim(), text.Trim().TrimStart(':', ',').Trim());

    // "bekleyen ajana izin ver", "allow the waiting agent": no name, whoever is waiting
    private static VoiceCommand Answer(VoiceCommandKind kind, Match m, IReadOnlyList<VoiceTarget> nodes) =>
        m.Groups["n"] is { Success: true } g && Waiting().IsMatch(g.Value) ? VoiceCommand.Of(kind) : Named(kind, m, nodes);

    private static VoiceCommand Named(VoiceCommandKind kind, Match m, IReadOnlyList<VoiceTarget> targets)
    {
        var name = m.Groups["n"] is { Success: true, Value.Length: > 0 } g ? g.Value.Trim() : null;
        return new VoiceCommand(kind, name == null ? [] : Match(name, targets), name);
    }

    private static string Span(string original, Group g) => g.Success ? original.Substring(g.Index, g.Length).Trim() : "";

    /// <summary>"testleri çalıştırmasını" → "testleri çalıştır", "düzeltsin" → "düzelt": what was asked, as an order.</summary>
    public static string Imperative(string text)
    {
        var m = ImperativeEnding().Match(text);
        return m.Success ? text[..m.Index] + m.Groups["stem"].Value : text;
    }

    // ---- Names ----

    /// <summary>The targets <paramref name="heard"/> may mean; the closest ones only.</summary>
    public static IReadOnlyList<object> Match(string heard, IReadOnlyList<VoiceTarget> targets)
    {
        var forms = Forms(Fold(heard)).ToList();
        if (forms.Count == 0) return [];
        var scored = targets
            .Select(t => (t.Key, Score: NameForms(t.Name).SelectMany(n => forms.Select(h => Math.Min(Distance(h, n), Distance(Sound(h), Sound(n))))).Min()))
            .Where(t => t.Score <= 2)
            .ToList();
        if (scored.Count == 0) return [];
        var best = scored.Min(t => t.Score);
        return scored.Where(t => t.Score == best).Select(t => t.Key).ToList();
    }

    // How close two names are: 0 = the same; long names may be a letter or two off ("klod" isn't "claude", though)
    private static int Distance(string heard, string name)
    {
        if (heard == name) return 0;
        var d = Levenshtein(heard, name);
        var allowed = name.Length >= 8 ? 2 : name.Length >= 4 ? 1 : 0;
        return d <= allowed ? d : int.MaxValue;
    }

    /// <summary>
    /// How a name sounds, roughly, so that a Turkish speaker's "Klod" is Claude and "Kodeks" is Codex: Whisper writes
    /// English names the way they're pronounced when the language is Turkish.
    /// </summary>
    public static string Sound(string folded)
    {
        var s = folded.Replace(" ", "").Replace("'", "").Replace("-", "");
        foreach (var (from, to) in Sounds) s = s.Replace(from, to);
        // Doubled letters and a silent final e ("claude" → "klod")
        s = Regex.Replace(s, @"(.)\1+", "$1");
        return s.Length > 3 && s.EndsWith('e') ? s[..^1] : s;
    }

    private static readonly (string, string)[] Sounds =
    [
        ("ph", "f"), ("th", "t"), ("sh", "s"), ("ch", "c"), ("ck", "k"), ("qu", "kv"),
        ("au", "o"), ("ou", "u"), ("ow", "o"), ("ai", "e"), ("ay", "ey"), ("ee", "i"), ("ea", "i"),
        ("x", "ks"), ("q", "k"), ("w", "v"), ("c", "k"), ("y", "i"), ("j", "c"),
    ];

    private static IEnumerable<string> NameForms(string name)
    {
        var f = Fold(name).Trim();
        yield return f;
        // "Claude (a1b2)" → "claude"; "Claude Code" → "claude"
        var bare = Regex.Replace(f, @"\s*\(.*\)\s*$", "");
        yield return bare;
        if (bare.Split(' ') is { Length: > 1 } words) yield return words[0];
    }

    // "the reviewer" → "reviewer"; "claude'a" → "claude"; "claudea" → "claude"/"claudea"
    private static IEnumerable<string> Forms(string heard)
    {
        var h = Regex.Replace(heard.Trim(), @"^the\s+", "");
        if (h.Length == 0) yield break;
        var apostrophe = h.IndexOf('\'');
        if (apostrophe > 0)
        {
            yield return h[..apostrophe];
            yield break;
        }
        yield return h;
        foreach (var suffix in CaseSuffixes)
            if (h.Length > suffix.Length + 2 && h.EndsWith(suffix)) yield return h[..^suffix.Length];
    }

    // Dative/accusative endings after folding (ı → i, ü → u): "Codex'e" may come as "codexe", "Gemini'ye" as "geminiye"
    private static readonly string[] CaseSuffixes = ["ya", "ye", "yi", "yu", "na", "ne", "ni", "nu", "a", "e", "i", "u"];

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// Lower case without Turkish letters or punctuation, the same length as <paramref name="text"/> so a match's
    /// position also points into the original ("Claude'a SÖYLE" → "claude'a soyle").
    /// </summary>
    public static string Fold(string text)
    {
        var lower = text.ToLower(Turkish);
        if (lower.Length != text.Length) lower = text.ToLowerInvariant();
        var s = new StringBuilder(lower.Length);
        foreach (var c in lower)
            s.Append(c switch
            {
                'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u',
                '’' or '‘' or '`' or '´' => '\'',
                '.' or ',' or '!' or '?' or ';' or ':' or '"' or '“' or '”' => ' ',
                _ => c,
            });
        return s.ToString();
    }

    // ---- Patterns (on folded text) ----

    private const string Verb = @"(?:\s+(?:git|gec|goster|ac|odaklan))";

    [GeneratedRegex(@"^(?:(?<n>.+?)\s+)?(?:izin\s+verme|reddet|hayir\s+de)\s*$")] private static partial Regex DenyTr();
    [GeneratedRegex(@"^(?:deny|reject|decline)(?:\s+(?:it|that|the\s+request))?(?:\s+(?:for\s+)?(?<n>.+?))?\s*$")] private static partial Regex DenyEn();
    [GeneratedRegex(@"^(?:(?<n>.+?)\s+)?(?:izin\s+ver|onayla|kabul\s+et|evet\s+de)\s*$")] private static partial Regex AllowTr();
    [GeneratedRegex(@"^(?:allow|approve|accept)(?:\s+(?:it|that|the\s+request))?(?:\s+(?:for\s+)?(?<n>.+?))?\s*$")] private static partial Regex AllowEn();

    [GeneratedRegex(@"\bbekle\w*|\bwaiting\b")] private static partial Regex Waiting();
    [GeneratedRegex(@"\b(?:hepsini|hepsi\s+ni|tumunu|hepsi|herseyi|her\s+seyi)\b.*\b(?:goster|sigdir|gor)|\bfit\s+(?:all|everything)\b|\bshow\s+(?:me\s+)?(?:all|everything)\b")] private static partial Regex FitAll();

    [GeneratedRegex(@"\b(?:ac|kapat|goster|gizle|show|hide|open|close|toggle)\b")] private static partial Regex PanelVerb();
    [GeneratedRegex(@"\b(?:pano\w*|board)\b")] private static partial Regex Board();
    [GeneratedRegex(@"\b(?:degisiklik\w*|changes)\b")] private static partial Regex Changes();
    [GeneratedRegex(@"\b(?:ajanlar\w*\s+panel\w*|ajanlar\w*|agents(?:\s+panel)?)\b")] private static partial Regex AgentsPanel();

    [GeneratedRegex(@"\b(?:sonraki|next)\b.*\b(?:alan\w*|workspace|tab)\b")] private static partial Regex NextWorkspace();
    [GeneratedRegex(@"\b(?:onceki|previous)\b.*\b(?:alan\w*|workspace|tab)\b")] private static partial Regex PreviousWorkspace();
    [GeneratedRegex(@"^(?<n>.+?)\s+(?:calisma\s+)?alan\w*" + Verb + @"\s*$")] private static partial Regex SwitchWorkspaceTr();
    [GeneratedRegex(@"^(?:switch|go)\s+to\s+(?:the\s+)?(?:workspace\s+(?<n>.+?)|(?<n>.+?)\s+workspace)\s*$")] private static partial Regex SwitchWorkspaceEn();

    [GeneratedRegex(@"^(?:notu?e?\s+al|not\s+yaz|yeni\s+not|take\s+a\s+note|new\s+note|note\s+down)(?:\s+(?:that\s+)?(?<t>.+))?\s*$")] private static partial Regex NewNote();
    [GeneratedRegex(@"^(?:yeni\s+(?:bir\s+)?terminal(?:\s+ac)?|terminal\s+ac|new\s+terminal|open\s+(?:a\s+)?(?:new\s+)?terminal)\s*$")] private static partial Regex NewTerminal();

    [GeneratedRegex(@"^(?:(?<n>.+?)\s+)?yakinlas\w*\s*$")] private static partial Regex ZoomTr();
    [GeneratedRegex(@"^zoom(?:\s+in)?(?:\s+(?:to|on)\s+(?<n>.+?))?\s*$")] private static partial Regex ZoomEn();

    [GeneratedRegex(@"^(?<n>\S+(?:\s+\S+)?)\s+(?:soyle|de\b|yaz|ilet)\s*(?<t>.+)$")] private static partial Regex TellTrNameFirst();
    [GeneratedRegex(@"^(?<t>.+)\s+(?<n>\S+)\s+(?:soyle|ilet)\s*$")] private static partial Regex TellTrNameLast();
    [GeneratedRegex(@"^(?<n>\S+)\s+(?<t>.+)\s+(?:soyle|ilet)\s*$")] private static partial Regex TellTrNameFirstVerbLast();
    [GeneratedRegex(@"^(?:tell|tel|ask)\s+(?<rest>.+)$")] private static partial Regex TellEn();
    [GeneratedRegex(@"\S+")] private static partial Regex WordSpans();

    [GeneratedRegex(@"^(?<n>.+?)\s+(?:kapat|sonlandir|oldur)\s*$")] private static partial Regex CloseTr();
    [GeneratedRegex(@"^(?:close|kill)\s+(?<n>.+?)\s*$")] private static partial Regex CloseEn();
    [GeneratedRegex(@"^(?<n>.+?)" + Verb + @"\s*$")] private static partial Regex GoToTr();
    [GeneratedRegex(@"^(?:go\s+to|show(?:\s+me)?|switch\s+to|focus(?:\s+on)?)\s+(?<n>.+?)\s*$")] private static partial Regex GoToEn();

    // Turkish verb nouns and the third-person imperative: "çalıştırmasını", "düzeltmesini", "baksın"
    [GeneratedRegex(@"(?<stem>\w+?)(?:masını|mesini|masına|mesine|mayı|meyi|ması|mesi|sın|sin|sun|sün)$")] private static partial Regex ImperativeEnding();
}
