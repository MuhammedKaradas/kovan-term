using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

public enum ToolKind { McpServer, Plugin, Skill, Subagent, Command, Hook }

/// <param name="Agent">"Claude Code" or "Codex".</param>
/// <param name="Source">Where it's defined: user, project, local (this folder only), system, or "plugin: name".</param>
/// <param name="Detail">A short description: a skill's description, a server's transport, a hook's matcher and command.</param>
/// <param name="Location">The file or folder it's defined in.</param>
public sealed record ToolItem(string Agent, ToolKind Kind, string Name, string Source, string Detail, string Location, bool Enabled = true)
{
    public string KindText => Kind switch
    {
        ToolKind.McpServer => "MCP servers",
        ToolKind.Plugin => "Plugins",
        ToolKind.Skill => "Skills",
        ToolKind.Subagent => "Subagents",
        ToolKind.Command => "Commands",
        _ => "Hooks",
    };
}

/// <summary>
/// What Claude Code and Codex have set up on this computer (#139): MCP servers, plugins, skills, subagents, commands
/// and hooks, for the user and for one project folder, read from the agents' own files. Read only. Kovan's own hook
/// (sessions outside Kovan) isn't listed; its per-run hooks and MCP server aren't in these files at all.
/// </summary>
public static partial class ToolsInventory
{
    public const string Claude = "Claude Code";
    public const string Codex = "Codex";

    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <param name="home">The user's home folder (tests pass their own).</param>
    /// <param name="project">The project folder to include project and local definitions of; <c>null</c> for the user's only.</param>
    public static IReadOnlyList<ToolItem> Scan(string home, string? project)
    {
        var items = new List<ToolItem>();
        ScanClaude(home, project, items);
        ScanCodex(home, project, items);
        return items;
    }

    // ---- Claude Code ----

    private static void ScanClaude(string home, string? project, List<ToolItem> items)
    {
        var claudeDir = Path.Combine(home, ".claude");
        var userSettings = Path.Combine(claudeDir, "settings.json");
        string? projectSettings = project != null ? Path.Combine(project, ".claude", "settings.json") : null;
        string? localSettings = project != null ? Path.Combine(project, ".claude", "settings.local.json") : null;

        // MCP servers: user and local ones in ~/.claude.json, project ones in .mcp.json
        var claudeJson = Path.Combine(home, ".claude.json");
        using (var doc = ReadJson(claudeJson))
        {
            if (doc?.RootElement is { ValueKind: JsonValueKind.Object } root)
            {
                AddMcpServers(items, Claude, root, "user", claudeJson);
                if (project != null && root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
                    foreach (var p in projects.EnumerateObject())
                        if (SamePath(p.Name, project) && p.Value.ValueKind == JsonValueKind.Object)
                            AddMcpServers(items, Claude, p.Value, "local", claudeJson);
            }
        }
        if (project != null)
        {
            var mcpJson = Path.Combine(project, ".mcp.json");
            using var doc = ReadJson(mcpJson);
            if (doc?.RootElement is { ValueKind: JsonValueKind.Object } root) AddMcpServers(items, Claude, root, "project", mcpJson);
        }

        // Plugins, and what the enabled ones bring
        var enabled = EnabledPlugins(userSettings);
        if (projectSettings != null) foreach (var (k, v) in EnabledPlugins(projectSettings)) enabled[k] = v;
        if (localSettings != null) foreach (var (k, v) in EnabledPlugins(localSettings)) enabled[k] = v;
        var installed = Path.Combine(claudeDir, "plugins", "installed_plugins.json");
        using (var doc = ReadJson(installed))
        {
            if (doc?.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("plugins", out var plugins)
                && plugins.ValueKind == JsonValueKind.Object)
            {
                foreach (var plugin in plugins.EnumerateObject())
                {
                    if (plugin.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var install in plugin.Value.EnumerateArray())
                    {
                        var scope = Str(install, "scope") ?? "user";
                        // Installed for another folder
                        if (scope != "user" && (project == null || Str(install, "projectPath") is not { } path || !SamePath(path, project))) continue;
                        var on = enabled.GetValueOrDefault(plugin.Name);
                        var name = plugin.Name.Split('@')[0];
                        var version = Str(install, "version") is { Length: > 0 } v && v != "unknown" ? $"version {v}" : "";
                        items.Add(new ToolItem(Claude, ToolKind.Plugin, plugin.Name, scope, Join(version, on ? "" : "disabled"), installed, on));
                        if (on && Str(install, "installPath") is { } dir && Directory.Exists(dir)) AddClaudeFolder(items, dir, $"plugin: {name}", name);
                    }
                }
            }
        }

        AddClaudeFolder(items, claudeDir, "user", null);
        // Skills synced from the claude.ai account: skills/synced/<account>/<skill>/SKILL.md
        foreach (var account in Subfolders(Path.Combine(claudeDir, "skills", "synced")).Where(d => !Path.GetFileName(d).StartsWith('.')))
            foreach (var skill in Subfolders(account))
                if (File.Exists(Path.Combine(skill, "SKILL.md")))
                    items.Add(Markdown(Claude, ToolKind.Skill, Path.Combine(skill, "SKILL.md"), Path.GetFileName(skill), "synced"));
        if (project != null) AddClaudeFolder(items, Path.Combine(project, ".claude"), "project", null);

        AddHooks(items, Claude, userSettings, "user");
        if (projectSettings != null) AddHooks(items, Claude, projectSettings, "project");
        if (localSettings != null) AddHooks(items, Claude, localSettings, "local");
    }

    /// <summary>Skills, subagents and commands in a <c>.claude</c> folder or a plugin; a plugin's hooks and MCP servers too.</summary>
    private static void AddClaudeFolder(List<ToolItem> items, string dir, string source, string? plugin)
    {
        var prefix = plugin != null ? $"{plugin}:" : "";
        foreach (var skill in Subfolders(Path.Combine(dir, "skills")))
            if (Path.GetFileName(skill) != "synced" && File.Exists(Path.Combine(skill, "SKILL.md")))
                items.Add(Markdown(Claude, ToolKind.Skill, Path.Combine(skill, "SKILL.md"), prefix + Path.GetFileName(skill), source));
        foreach (var agent in Files(Path.Combine(dir, "agents"), "*.md", SearchOption.TopDirectoryOnly))
            items.Add(Markdown(Claude, ToolKind.Subagent, agent, Path.GetFileNameWithoutExtension(agent), source));
        var commands = Path.Combine(dir, "commands");
        foreach (var command in Files(commands, "*.md", SearchOption.AllDirectories))
        {
            // A subfolder is a namespace: commands/git/commit.md is /git:commit
            var relative = Path.GetRelativePath(commands, command);
            var name = Path.ChangeExtension(relative, null).Replace(Path.DirectorySeparatorChar, ':');
            items.Add(Markdown(Claude, ToolKind.Command, command, "/" + prefix + name, source, nameFromFile: false));
        }
        if (plugin != null)
        {
            AddHooks(items, Claude, Path.Combine(dir, "hooks", "hooks.json"), source);
            var mcpJson = Path.Combine(dir, ".mcp.json");
            using var doc = ReadJson(mcpJson);
            if (doc?.RootElement is { ValueKind: JsonValueKind.Object } root)
            {
                // Either { "mcpServers": { … } } or the servers themselves
                if (root.TryGetProperty("mcpServers", out _)) AddMcpServers(items, Claude, root, source, mcpJson);
                else AddServers(items, Claude, root, source, mcpJson);
            }
        }
    }

    private static Dictionary<string, bool> EnabledPlugins(string settingsFile)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        using var doc = ReadJson(settingsFile);
        if (doc?.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("enabledPlugins", out var plugins)
            && plugins.ValueKind == JsonValueKind.Object)
            foreach (var p in plugins.EnumerateObject())
                result[p.Name] = p.Value.ValueKind == JsonValueKind.True;
        return result;
    }

    // ---- Codex ----

    private static void ScanCodex(string home, string? project, List<ToolItem> items)
    {
        var codexDir = Path.Combine(home, ".codex");
        var config = Path.Combine(codexDir, "config.toml");
        foreach (var (kind, name, values) in TomlTables(config))
        {
            if (kind == "mcp_servers")
            {
                var detail = values.TryGetValue("url", out var url) ? $"http · {Host(url)}"
                    : values.TryGetValue("command", out var command) ? $"stdio · {Path.GetFileName(command)}" : "";
                var on = !values.TryGetValue("enabled", out var e) || e != "false";
                items.Add(new ToolItem(Codex, ToolKind.McpServer, name, "user", Join(detail, on ? "" : "disabled"), config, on));
            }
            else if (kind == "plugins")
            {
                var on = values.TryGetValue("enabled", out var e) && e == "true";
                items.Add(new ToolItem(Codex, ToolKind.Plugin, name, "user", on ? "" : "disabled", config, on));
            }
        }

        var skills = Path.Combine(codexDir, "skills");
        foreach (var skill in Subfolders(skills))
        {
            // Codex's own skills are in .system
            if (Path.GetFileName(skill) == ".system")
            {
                foreach (var system in Subfolders(skill))
                    if (File.Exists(Path.Combine(system, "SKILL.md")))
                        items.Add(Markdown(Codex, ToolKind.Skill, Path.Combine(system, "SKILL.md"), Path.GetFileName(system), "system"));
            }
            else if (File.Exists(Path.Combine(skill, "SKILL.md")))
                items.Add(Markdown(Codex, ToolKind.Skill, Path.Combine(skill, "SKILL.md"), Path.GetFileName(skill), "user"));
        }
        if (project != null)
            foreach (var folder in new[] { Path.Combine(project, ".codex", "skills"), Path.Combine(project, ".agents", "skills") })
                foreach (var skill in Subfolders(folder))
                    if (File.Exists(Path.Combine(skill, "SKILL.md")))
                        items.Add(Markdown(Codex, ToolKind.Skill, Path.Combine(skill, "SKILL.md"), Path.GetFileName(skill), "project"));

        foreach (var prompt in Files(Path.Combine(codexDir, "prompts"), "*.md", SearchOption.TopDirectoryOnly))
            items.Add(Markdown(Codex, ToolKind.Command, prompt, "/prompts:" + Path.GetFileNameWithoutExtension(prompt), "user", nameFromFile: false));

        AddHooks(items, Codex, Path.Combine(codexDir, "hooks.json"), "user");
        if (project != null) AddHooks(items, Codex, Path.Combine(project, ".codex", "hooks.json"), "project");
    }

    /// <summary>
    /// The tables of a TOML file that Kovan lists, <c>[mcp_servers.name]</c> and <c>[plugins."name"]</c>, with their simple
    /// <c>key = value</c> lines. Not a TOML parser: enough for these tables (sub-tables such as <c>.env</c> are skipped).
    /// </summary>
    public static IEnumerable<(string Kind, string Name, Dictionary<string, string> Values)> TomlTables(string path)
    {
        string[] lines;
        try { lines = File.Exists(path) ? File.ReadAllLines(path) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        (string Kind, string Name, Dictionary<string, string> Values)? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                if (current is { } done) yield return done;
                current = TableHeader().Match(line) is { Success: true } m ? (m.Groups[1].Value, Unquote(m.Groups[2].Value), new Dictionary<string, string>()) : null;
                continue;
            }
            if (current is { } table && KeyValue().Match(line) is { Success: true } kv)
                table.Values[kv.Groups[1].Value] = Unquote(kv.Groups[2].Value.Trim());
        }
        if (current is { } last) yield return last;
    }

    // [mcp_servers.name] or [plugins."name@market"], but not [mcp_servers.name.env]
    [GeneratedRegex("""^\[(mcp_servers|plugins)\.("[^"]+"|'[^']+'|[A-Za-z0-9_-]+)\]$""")]
    private static partial Regex TableHeader();

    [GeneratedRegex("""^([A-Za-z0-9_-]+)\s*=\s*(.+)$""")]
    private static partial Regex KeyValue();

    private static string Unquote(string s) =>
        s.Length >= 2 && (s[0] == '"' && s[^1] == '"' || s[0] == '\'' && s[^1] == '\'') ? s[1..^1] : s;

    // ---- Shared ----

    private static void AddMcpServers(List<ToolItem> items, string agent, JsonElement owner, string source, string location)
    {
        if (owner.TryGetProperty("mcpServers", out var servers) && servers.ValueKind == JsonValueKind.Object)
            AddServers(items, agent, servers, source, location);
    }

    private static void AddServers(List<ToolItem> items, string agent, JsonElement servers, string source, string location)
    {
        foreach (var s in servers.EnumerateObject())
        {
            if (s.Value.ValueKind != JsonValueKind.Object) continue;
            // Only the transport and the program or host: arguments, headers and env may carry keys
            var detail = Str(s.Value, "url") is { } url ? $"{Str(s.Value, "type") ?? "http"} · {Host(url)}"
                : Str(s.Value, "command") is { } command ? $"stdio · {Path.GetFileName(command)}" : "";
            if (detail.Contains("KovanLaunch", StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new ToolItem(agent, ToolKind.McpServer, s.Name, source, detail, location));
        }
    }

    /// <summary>A settings or hooks.json file's hooks: one item per command, named after its event.</summary>
    private static void AddHooks(List<ToolItem> items, string agent, string file, string source)
    {
        using var doc = ReadJson(file);
        if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("hooks", out var hooks)
            || hooks.ValueKind != JsonValueKind.Object)
            return;
        foreach (var e in hooks.EnumerateObject())
        {
            if (e.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var matcher in e.Value.EnumerateArray())
            {
                if (matcher.ValueKind != JsonValueKind.Object || !matcher.TryGetProperty("hooks", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                var match = Str(matcher, "matcher") is { Length: > 0 } m ? $"{m} · " : "";
                foreach (var hook in list.EnumerateArray())
                {
                    if (hook.ValueKind != JsonValueKind.Object) continue;
                    var command = Str(hook, "command") ?? Str(hook, "url") ?? Str(hook, "prompt") ?? Str(hook, "type") ?? "";
                    // Kovan's own: the hook for sessions outside Kovan
                    if (OutsideHook.IsKovanCommand(command)) continue;
                    items.Add(new ToolItem(agent, ToolKind.Hook, e.Name, source, match + Shorten(command, 90), file));
                }
            }
        }
    }

    /// <summary>A skill, subagent or command file: its name (front matter <c>name:</c>, or the file's) and description.</summary>
    private static ToolItem Markdown(string agent, ToolKind kind, string file, string fallbackName, string source, bool nameFromFile = true)
    {
        var (name, description) = FrontMatter(file);
        return new ToolItem(agent, kind, nameFromFile && name is { Length: > 0 } n ? n : fallbackName, source, Shorten(description ?? "", 140), file);
    }

    /// <summary>The <c>name</c> and <c>description</c> of a Markdown file's YAML front matter.</summary>
    public static (string? Name, string? Description) FrontMatter(string file)
    {
        try
        {
            using var reader = new StreamReader(file);
            if (reader.ReadLine()?.Trim() != "---") return (null, null);
            string? name = null, description = null;
            // A block value (description: >- and indented lines under it) is joined into one line
            List<string>? block = null;
            for (var i = 0; i < 60 && reader.ReadLine() is { } line; i++)
            {
                if (line.Trim() == "---") break;
                if (block != null && line.Length > 0 && char.IsWhiteSpace(line[0]))
                {
                    block.Add(line.Trim());
                    continue;
                }
                if (block != null)
                {
                    description = string.Join(' ', block);
                    block = null;
                }
                if (line.StartsWith("name:", StringComparison.Ordinal)) name = Unquote(line[5..].Trim());
                else if (line.StartsWith("description:", StringComparison.Ordinal))
                {
                    var value = line[12..].Trim();
                    if (value is ">" or "|" or ">-" or "|-" or ">+" or "|+") block = [];
                    else description = Unquote(value);
                }
            }
            if (block != null) description = string.Join(' ', block);
            return (name, description);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null); }
    }

    private static JsonDocument? ReadJson(string path)
    {
        try { return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path), Lenient) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static IEnumerable<string> Subfolders(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir).Order(StringComparer.OrdinalIgnoreCase) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> Files(string dir, string pattern, SearchOption option)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, option).Order(StringComparer.OrdinalIgnoreCase) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => p.Length > 0));

    private static string Shorten(string s, int max)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length > max ? s[..(max - 1)] + "…" : s;
    }

    /// <summary>~/.claude.json writes project folders with forward slashes.</summary>
    public static bool SamePath(string a, string b)
    {
        static string N(string p) => p.Replace('/', '\\').TrimEnd('\\');
        return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
    }
}
