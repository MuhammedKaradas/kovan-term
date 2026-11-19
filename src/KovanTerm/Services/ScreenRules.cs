using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>One screen rule (see <c>Resources/screen-rules.json</c>).</summary>
public sealed record ScreenRule
{
    public required string Id { get; init; }
    /// <summary>Which agent's screen it applies to: <c>claude</c>, <c>codex</c>, <c>gemini</c>…</summary>
    public required string Agent { get; init; }
    /// <summary><c>blocked</c>, <c>working</c> or <c>idle</c>.</summary>
    public required string State { get; init; }
    /// <summary>For blocked: <c>permission</c> (Allow/Deny can answer it), <c>trust</c>, <c>question</c>.</summary>
    public string? Kind { get; init; }
    /// <summary>Text shown on the node, e.g. "Trust this folder?".</summary>
    public string? Detail { get; init; }
    public int Priority { get; init; }
    /// <summary>Every phrase must appear.</summary>
    public string[] All { get; init; } = [];
    /// <summary>At least one phrase must appear (if any are given).</summary>
    public string[] Any { get; init; } = [];
    /// <summary>None of these may appear.</summary>
    public string[] None { get; init; } = [];
}

/// <summary>
/// Reads an agent's state from the bottom of its terminal screen, for agents (or prompts) that report nothing
/// themselves: Gemini CLI, folder trust prompts, Codex's hook review. Data-driven, so a CLI changing its wording
/// means editing a JSON file; captured screens in the tests catch it.
/// </summary>
public sealed partial class ScreenRules
{
    /// <summary>How much of the screen's bottom is looked at: prompts and status lines live there.</summary>
    public const int RegionLines = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record RuleFile(List<ScreenRule> Rules);

    private readonly Dictionary<string, List<ScreenRule>> _byAgent;

    public ScreenRules(IEnumerable<ScreenRule> rules)
    {
        _byAgent = rules
            .Select(r => r with
            {
                All = r.All.Select(Normalize).ToArray(),
                Any = r.Any.Select(Normalize).ToArray(),
                None = r.None.Select(Normalize).ToArray(),
            })
            .GroupBy(r => r.Agent, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Priority).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public bool HasRulesFor(string? agent) => agent != null && _byAgent.ContainsKey(agent);

    /// <summary>The built-in rules, with the user's file (same id replaces, new ids are added) on top.</summary>
    public static ScreenRules Load(string? userFile = null)
    {
        using var stream = typeof(ScreenRules).Assembly.GetManifestResourceStream("KovanTerm.Resources.screen-rules.json")
            ?? throw new InvalidOperationException("screen-rules.json resource missing");
        var rules = Parse(new StreamReader(stream).ReadToEnd());
        userFile ??= Path.Combine(AppPaths.DataDirectory, "screen-rules.json");
        if (File.Exists(userFile))
        {
            try
            {
                foreach (var user in Parse(File.ReadAllText(userFile)))
                {
                    rules.RemoveAll(r => r.Id == user.Id);
                    rules.Add(user);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Debug.WriteLine($"Couldn't read user screen rules, using the built-in ones: {ex.Message}");
            }
        }
        return new ScreenRules(rules);
    }

    public static List<ScreenRule> Parse(string json) => JsonSerializer.Deserialize<RuleFile>(json, JsonOptions)?.Rules ?? [];

    /// <summary>The highest-priority rule matching the bottom of the screen; <c>null</c> = no opinion.</summary>
    public ScreenRule? Match(string? agent, string screen)
    {
        if (agent == null || !_byAgent.TryGetValue(agent, out var rules)) return null;
        var text = Normalize(BottomLines(screen, RegionLines));
        return rules.FirstOrDefault(r =>
            r.All.All(p => text.Contains(p, StringComparison.Ordinal)) &&
            (r.Any.Length == 0 || r.Any.Any(p => text.Contains(p, StringComparison.Ordinal))) &&
            !r.None.Any(p => text.Contains(p, StringComparison.Ordinal)));
    }

    /// <summary>What a match means for the node: waiting (with Allow/Deny for permissions), working or idle.</summary>
    public static AgentStatus? ToStatus(ScreenRule? rule) => rule?.State switch
    {
        "blocked" => new AgentStatus(NodeStatus.Waiting, rule.Detail, AwaitsPermission: rule.Kind == "permission"),
        "working" => new AgentStatus(NodeStatus.Working, rule.Detail),
        "idle" => AgentStatus.Idle,
        _ => null,
    };

    private static string BottomLines(string screen, int count)
    {
        var lines = screen.Replace("\r\n", "\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Count - count)));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Lower case, line breaks and runs of spaces as one space: a phrase wrapped across lines still matches.</summary>
    public static string Normalize(string text) => Whitespace().Replace(text.ToLowerInvariant(), " ").Trim();

    /// <summary>
    /// Which agent a command line runs, from its executable's name (<c>claude</c>, <c>codex</c>, <c>gemini</c>…),
    /// looking inside <c>cmd /k "…"</c> wrappers; <c>null</c> for anything else.
    /// </summary>
    public static string? AgentOf(string commandLine)
    {
        var exe = CommandResolver.FirstToken(commandLine);
        if (Path.GetFileNameWithoutExtension(exe).Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            var k = commandLine.IndexOf(" /k ", StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            var inner = commandLine[(k + 4)..].Trim();
            // cmd.exe /k "C:\npm\claude.cmd --x"  or  cmd.exe /k ""C:\Program Files\…\claude.cmd" --x"
            if (inner.StartsWith("\"\"", StringComparison.Ordinal)) inner = inner[1..];
            else if (inner.StartsWith('"')) inner = inner[1..].TrimEnd('"');
            exe = CommandResolver.FirstToken(inner);
        }
        return AgentOfExecutable(exe);
    }

    /// <summary>Which agent an executable is, from its file name (a path, possibly with spaces); <c>null</c> otherwise.</summary>
    public static string? AgentOfExecutable(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name switch
        {
            "claude" or "codex" or "gemini" => name,
            "cursor-agent" => "cursor",
            "agy" => "antigravity",
            _ => null,
        };
    }
}
