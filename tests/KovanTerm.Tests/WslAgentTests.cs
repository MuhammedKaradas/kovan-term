using System.Text.Json;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class WslAgentTests
{
    private const string Endpoint = "/mnt/c/Users/me/AppData/Roaming/KovanTerm/hooks/n.endpoint";
    private const string Curl = "/mnt/c/WINDOWS/system32/curl.exe";

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\KovanTerm\hooks\a.json", "/mnt/c/Users/me/AppData/Roaming/KovanTerm/hooks/a.json")]
    [InlineData(@"F:\data dir\x", "/mnt/f/data dir/x")]
    public void ToWslPath_UsesTheDefaultMounts(string windows, string linux) =>
        Assert.Equal(linux, ClaudeHooks.ToWslPath(windows));

    [Fact]
    public void WslSettings_PostEveryEventWithWindowsCurl_ReadingTheEndpointEachTime()
    {
        using var doc = JsonDocument.Parse(ClaudeHooks.BuildWslSettings(Endpoint, Curl));
        var hooks = doc.RootElement.GetProperty("hooks");

        Assert.Equal(ClaudeHooks.Events.Count, hooks.EnumerateObject().Count());
        var hook = hooks.GetProperty("UserPromptSubmit")[0].GetProperty("hooks")[0];
        Assert.Equal("command", hook.GetProperty("type").GetString());
        var command = hook.GetProperty("command").GetString()!;
        Assert.StartsWith($"[ -r '{Endpoint}' ] && '{Curl}' -s", command);
        // URL (line 1) and token (line 2) come from the file, so a running agent follows Kovan's new port
        Assert.Contains($"\"$(sed -n 1p '{Endpoint}')\"", command);
        Assert.Contains($"\"{AgentHookServer.TokenHeader}: $(sed -n 2p '{Endpoint}')\"", command);
        Assert.Contains("--data-binary @-", command);
        // Kovan not running must not show an error in the agent
        Assert.EndsWith("|| true", command);
    }

    [Fact]
    public void WslMcpConfig_BridgesStdioToTheMcpEndpoint()
    {
        using var doc = JsonDocument.Parse(ClaudeHooks.BuildWslMcpConfig(Endpoint, Curl));
        var kovan = doc.RootElement.GetProperty("mcpServers").GetProperty("kovan");

        Assert.Equal("stdio", kovan.GetProperty("type").GetString());
        Assert.Equal("sh", kovan.GetProperty("command").GetString());
        var args = kovan.GetProperty("args");
        Assert.Equal("-c", args[0].GetString());
        var bridge = args[1].GetString()!;
        Assert.StartsWith("while IFS= read -r line; do [ -z \"$line\" ] && continue;", bridge);
        Assert.Contains($"\"$(sed -n 3p '{Endpoint}')\"", bridge);
        Assert.Contains("printf '%s\\n' \"$r\"", bridge);
    }

    [Fact]
    public void WslCodex_ReachesTheMeshThroughTheBridgeScript_WithoutDoubleQuotes()
    {
        var args = ClaudeHooks.CodexWslMcpArguments("/mnt/f/data dir/hooks/n.wsl.mcp.sh");

        // Each override is one shell word; inside it, TOML literal strings
        Assert.Equal(@"-c 'mcp_servers.kovan.command='\''sh'\''' -c 'mcp_servers.kovan.args=['\''/mnt/f/data dir/hooks/n.wsl.mcp.sh'\'']'", args);
        Assert.DoesNotContain('"', args);
        // The script is the same bridge Claude Code's MCP config runs
        using var doc = JsonDocument.Parse(ClaudeHooks.BuildWslMcpConfig(Endpoint, Curl));
        Assert.Equal(ClaudeHooks.WslMcpBridge(Endpoint, Curl),
            doc.RootElement.GetProperty("mcpServers").GetProperty("kovan").GetProperty("args")[1].GetString());
    }

    [Fact]
    public void WslCodexNode_IsCodex()
    {
        var profile = new TerminalProfile { Id = "codex-wsl-Debian", Name = "c", Executable = "codex", Wsl = true, Distro = "Debian" };
        var node = TerminalNodeViewModel.FromState(new NodeState { Title = "c", CommandLine = @"C:\Windows\System32\wsl.exe -d Debian", Wsl = true },
            profile: profile);

        Assert.Equal("codex", node.AgentKind);
        Assert.Equal("y", node.AllowKey);
    }

    [Fact]
    public void WslSessionArguments_LetTheShellChooseResumeOrNew()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Equal($"$(ls ~/.claude/projects/*/{id}.jsonl >/dev/null 2>&1 && echo --resume || echo --session-id) {id}",
            ClaudeSessions.WslArguments(id, ""));
        Assert.Equal("", ClaudeSessions.WslArguments(id, "--continue"));
        Assert.Equal("", ClaudeSessions.WslArguments("not-a-guid; rm -rf ~", ""));
    }

    [Fact]
    public void WslNode_KnowsItsAgent_AndReadsItsTranscriptThroughTheDistro()
    {
        var profile = new TerminalProfile { Id = "claude-wsl-Debian", Name = "c", Executable = "claude", Wsl = true, Distro = "Debian" };
        var node = TerminalNodeViewModel.FromState(new NodeState { Title = "c", CommandLine = @"C:\Windows\System32\wsl.exe -d Debian", Wsl = true },
            profile: profile);
        node.ApplyAgentEvent(new AgentEvent("SessionStart", null, null, null, "/home/me/.claude/projects/x/s.jsonl", null, null));

        Assert.Equal("claude", node.AgentKind);
        Assert.Equal(@"\\wsl.localhost\Debian\home\me\.claude\projects\x\s.jsonl", node.ReadableTranscriptPath);
    }
}
