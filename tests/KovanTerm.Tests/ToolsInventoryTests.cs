using System.IO;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>The Tools window's list: read from a made-up home folder and project (#139).</summary>
public sealed class ToolsInventoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private string Home => Path.Combine(_dir, "home");
    private string Project => Path.Combine(_dir, "app");
    private string PluginDir => Path.Combine(Home, ".claude", "plugins", "cache", "market", "helper", "1.0");

    public ToolsInventoryTests()
    {
        Write(Path.Combine(Home, ".claude.json"), $$"""
            {
              "mcpServers": { "chrome": { "type": "stdio", "command": "C:\\tools\\npx.cmd", "args": ["--token", "SECRET"] } },
              "projects": { "{{Project.Replace('\\', '/')}}": { "mcpServers": { "pw": { "command": "npx" } } },
                            "C:/elsewhere": { "mcpServers": { "other": { "command": "x" } } } }
            }
            """);
        Write(Path.Combine(Home, ".claude", "settings.json"), $$"""
            {
              "enabledPlugins": { "helper@market": true, "off@market": false },
              "hooks": {
                "Stop": [ { "hooks": [ { "type": "command", "command": "notify.exe" } ] } ],
                "SessionStart": [ { "hooks": [ { "type": "command", "command": "\"C:/K/KovanLaunch.exe\" --hook \"C:/D/outside.endpoint\"" } ] } ]
              }
            }
            """);
        Write(Path.Combine(Home, ".claude", "plugins", "installed_plugins.json"), $$"""
            { "version": 2, "plugins": {
                "helper@market": [ { "scope": "user", "installPath": "{{PluginDir.Replace("\\", "\\\\")}}", "version": "1.0" } ],
                "off@market": [ { "scope": "user", "installPath": "C:\\none" } ],
                "mine@market": [ { "scope": "local", "projectPath": "C:\\elsewhere", "installPath": "C:\\none" } ] } }
            """);
        Write(Path.Combine(PluginDir, "skills", "lint", "SKILL.md"), "---\nname: lint\ndescription: Lints things\n---\n");
        Write(Path.Combine(PluginDir, "commands", "fix.md"), "Fix it");
        Write(Path.Combine(PluginDir, "hooks", "hooks.json"), """{ "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "check.sh" } ] } ] } }""");
        Write(Path.Combine(PluginDir, ".mcp.json"), """{ "db": { "type": "http", "url": "https://db.example.com/mcp?key=SECRET" } }""");
        Write(Path.Combine(Home, ".claude", "skills", "notes", "SKILL.md"), "---\nname: notes\ndescription: \"Takes notes\"\n---\nbody");
        Write(Path.Combine(Home, ".claude", "skills", "synced", "acct", "pdf", "SKILL.md"), "---\nname: pdf\ndescription: PDFs\n---\n");
        Write(Path.Combine(Home, ".claude", "agents", "reviewer.md"), "---\nname: reviewer\ndescription: Reviews code\n---\n");
        Write(Path.Combine(Home, ".claude", "commands", "git", "commit.md"), "---\ndescription: Commit\n---\n");
        Write(Path.Combine(Project, ".mcp.json"), """{ "mcpServers": { "local-db": { "command": "db-server" } } }""");
        Write(Path.Combine(Project, ".claude", "agents", "tester.md"), "# no front matter");
        Write(Path.Combine(Project, ".claude", "settings.local.json"), """{ "hooks": { "PostToolUse": [ { "hooks": [ { "type": "command", "command": "fmt" } ] } ] } }""");

        Write(Path.Combine(Home, ".codex", "config.toml"), """
            model = "gpt-5"

            [mcp_servers.node_repl]
            command = 'C:\tools\node.exe'
            args = ["--secret"]

            [mcp_servers.node_repl.env]
            TOKEN = "SECRET"

            [mcp_servers.remote]
            url = "https://mcp.example.com/sse"
            enabled = false

            [plugins."browser@openai"]
            enabled = true

            [plugins."docs@openai"]
            enabled = false
            """);
        Write(Path.Combine(Home, ".codex", "skills", ".system", "imagegen", "SKILL.md"), "---\nname: imagegen\ndescription: Images\n---\n");
        Write(Path.Combine(Home, ".codex", "skills", "deploy", "SKILL.md"), "---\nname: deploy\ndescription: Deploys\n---\n");
        Write(Path.Combine(Home, ".codex", "prompts", "review.md"), "Review");
        Write(Path.Combine(Home, ".codex", "hooks.json"), """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "beep" } ] } ] } }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private IReadOnlyList<ToolItem> Scan(bool withProject = true) => ToolsInventory.Scan(Home, withProject ? Project : null);

    private static string[] Names(IEnumerable<ToolItem> items, string agent, ToolKind kind) =>
        items.Where(i => i.Agent == agent && i.Kind == kind).Select(i => $"{i.Name} [{i.Source}]").Order().ToArray();

    [Fact]
    public void Claude_code_s_tools_for_the_user_and_the_project()
    {
        var items = Scan();
        const string c = ToolsInventory.Claude;

        Assert.Equal(["chrome [user]", "db [plugin: helper]", "local-db [project]", "pw [local]"], Names(items, c, ToolKind.McpServer));
        Assert.Equal(["helper@market [user]", "off@market [user]"], Names(items, c, ToolKind.Plugin));
        Assert.False(items.Single(i => i.Name == "off@market").Enabled);
        Assert.Equal(["lint [plugin: helper]", "notes [user]", "pdf [synced]"], Names(items, c, ToolKind.Skill));
        Assert.Equal(["reviewer [user]", "tester [project]"], Names(items, c, ToolKind.Subagent));
        Assert.Equal(["/git:commit [user]", "/helper:fix [plugin: helper]"], Names(items, c, ToolKind.Command));
        // Kovan's own hook for sessions outside Kovan isn't the user's
        Assert.Equal(["PostToolUse [local]", "PreToolUse [plugin: helper]", "Stop [user]"], Names(items, c, ToolKind.Hook));
        Assert.Equal("Bash · check.sh", items.Single(i => i.Name == "PreToolUse").Detail);
        Assert.Equal("Takes notes", items.Single(i => i.Name == "notes").Detail);
    }

    [Fact]
    public void Without_a_project_only_the_user_s()
    {
        var items = Scan(withProject: false);

        Assert.Equal(["chrome [user]", "db [plugin: helper]"], Names(items, ToolsInventory.Claude, ToolKind.McpServer));
        Assert.Equal(["reviewer [user]"], Names(items, ToolsInventory.Claude, ToolKind.Subagent));
    }

    [Fact]
    public void Codex_s_tools()
    {
        var items = Scan();
        const string x = ToolsInventory.Codex;

        Assert.Equal(["node_repl [user]", "remote [user]"], Names(items, x, ToolKind.McpServer));
        Assert.Equal("stdio · node.exe", items.Single(i => i.Name == "node_repl").Detail);
        Assert.False(items.Single(i => i.Name == "remote").Enabled);
        Assert.Equal(["browser@openai [user]", "docs@openai [user]"], Names(items, x, ToolKind.Plugin));
        Assert.Equal(["deploy [user]", "imagegen [system]"], Names(items, x, ToolKind.Skill));
        Assert.Equal(["/prompts:review [user]"], Names(items, x, ToolKind.Command));
        Assert.Equal(["Stop [user]"], Names(items, x, ToolKind.Hook));
    }

    [Fact]
    public void Arguments_env_and_url_queries_aren_t_shown()
    {
        var shown = string.Join("\n", Scan().Select(i => i.Name + " " + i.Detail));

        Assert.DoesNotContain("SECRET", shown);
        Assert.Contains("http · db.example.com", shown);
    }

    [Fact]
    public async Task The_window_groups_counts_and_filters()
    {
        var vm = new ToolsViewModel(p => ToolsInventory.Scan(Home, p), Project);
        await vm.LoadAsync();

        Assert.Contains(vm.Groups, g => g.Title == "MCP servers (4)");
        vm.Filter = "db";
        Assert.Equal(["MCP servers (2 of 4)"], vm.Groups.Select(g => g.Title));
        vm.Filter = "nothing like it";
        Assert.Empty(vm.Groups);
        Assert.Equal("Nothing matches the filter.", vm.Summary);

        vm.Filter = "";
        vm.Agent = ToolsInventory.Codex;
        Assert.Contains(vm.Groups, g => g.Title == "Plugins (2)");
    }

    [Fact]
    public void A_block_description_is_read_as_one_line()
    {
        var file = Path.Combine(_dir, "SKILL.md");
        Write(file, "---\nname: challenge\ndescription: >-\n  Pokes holes\n  in a plan.\nmodel: opus\n---\nbody");

        Assert.Equal(("challenge", "Pokes holes in a plan."), ToolsInventory.FrontMatter(file));
    }
}
