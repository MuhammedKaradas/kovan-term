using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class ScreenRulesTests : IDisposable
{
    private static readonly ScreenRules Rules = ScreenRules.Load(userFile: "does-not-exist.json");
    private readonly string _userFile = Path.Combine(Path.GetTempPath(), $"kovan-rules-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_userFile);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ScreenFixtures", name + ".screen"));

    // Captured from real terminals (UI Automation text of the visible screen); gemini-* are synthetic
    [Theory]
    [InlineData("claude", "claude-trust", "claude-trust")]
    [InlineData("claude", "claude-permission", "claude-permission")]
    [InlineData("claude", "claude-working", "claude-working")]
    [InlineData("claude", "claude-idle", null)]
    [InlineData("codex", "codex-trust", "codex-trust")]
    [InlineData("codex", "codex-approval", "codex-approval")]
    [InlineData("codex", "codex-approval-2", "codex-approval")]
    [InlineData("codex", "codex-working", "codex-working")] // also shows the idle placeholder: working wins
    [InlineData("gemini", "gemini-trust", "gemini-trust")]
    [InlineData("gemini", "gemini-permission", "gemini-permission")]
    [InlineData("gemini", "gemini-working", "gemini-working")]
    [InlineData("gemini", "gemini-idle", "gemini-idle")]
    public void Fixtures_MatchTheExpectedRule(string agent, string fixture, string? expectedRule) =>
        Assert.Equal(expectedRule, Rules.Match(agent, Fixture(fixture))?.Id);

    [Fact]
    public void RulesOnlyApplyToTheirAgent()
    {
        Assert.Null(Rules.Match("codex", Fixture("claude-trust")));
        Assert.Null(Rules.Match(null, Fixture("claude-trust")));
        Assert.False(Rules.HasRulesFor("powershell"));
        Assert.True(Rules.HasRulesFor("gemini"));
    }

    [Fact]
    public void WrappedPhrases_StillMatch()
    {
        // "Do you want to" split over two lines, odd spacing and case
        Assert.Equal("claude-permission", Rules.Match("claude", "Do you want\n   to  create a.txt?\n ESC to cancel")?.Id);
    }

    [Fact]
    public void OnlyTheBottomOfTheScreenCounts()
    {
        // An old prompt scrolled far up is history, not the current state
        var screen = "Do you want to create x?\nEsc to cancel\n" + string.Join("\n", Enumerable.Range(1, 40).Select(i => $"output line {i}"));
        Assert.Null(Rules.Match("claude", screen));
    }

    [Fact]
    public void UserRules_ReplaceAndAdd()
    {
        File.WriteAllText(_userFile, """
            { "rules": [
              { "id": "claude-working", "agent": "claude", "state": "working", "priority": 50, "any": ["thinking hard"] },
              { "id": "aider-waiting", "agent": "aider", "state": "blocked", "kind": "question", "priority": 80, "any": ["(y)es/(n)o"] }
            ] }
            """);
        var rules = ScreenRules.Load(_userFile);
        Assert.Null(rules.Match("claude", "esc to interrupt"));
        Assert.Equal("claude-working", rules.Match("claude", "Thinking hard…")?.Id);
        Assert.Equal("aider-waiting", rules.Match("aider", "Apply edit? (Y)es/(N)o")?.Id);
    }

    [Theory]
    [InlineData("cmd.exe /k \"C:\\npm\\claude.cmd --model opus\"", "claude")]
    [InlineData("cmd.exe /k \"\"C:\\Program Files\\nodejs\\claude.cmd\" --settings \"C:\\x y\\h.json\"\"", "claude")]
    [InlineData("\"C:\\tools\\codex.exe\" --no-daemon", "codex")]
    [InlineData("gemini", "gemini")]
    [InlineData("powershell.exe -NoLogo", null)]
    [InlineData("cmd.exe", null)]
    public void AgentOf_LooksInsideCmdWrappers(string commandLine, string? agent) =>
        Assert.Equal(agent, ScreenRules.AgentOf(commandLine));

    [Theory]
    [InlineData(@"C:\Program Files\nodejs\claude.cmd", "claude")] // spaces: a path, not a command line
    [InlineData(@"C:\Users\me\AppData\Roaming\npm\codex.CMD", "codex")]
    [InlineData(@"C:\Windows\System32\cmd.exe", null)]
    public void AgentOfExecutable_UsesTheFileName(string path, string? agent) =>
        Assert.Equal(agent, ScreenRules.AgentOfExecutable(path));

    [Fact]
    public void Screen_FillsGaps_ButReportsWin()
    {
        var working = new AgentStatus(NodeStatus.Working, "Bash");
        var trust = new AgentStatus(NodeStatus.Waiting, "Trust this folder?");
        // A prompt nothing reported shows as waiting
        Assert.Equal(trust, TerminalNodeViewModel.Combine(AgentStatus.Idle, trust, agentReports: true));
        // Otherwise the agent's own reports win over the screen
        Assert.Equal(working, TerminalNodeViewModel.Combine(working, AgentStatus.Idle, agentReports: true));
        // An agent that reports nothing is shown from its screen
        Assert.Equal(working, TerminalNodeViewModel.Combine(AgentStatus.Idle, working, agentReports: false));
    }

    [Fact]
    public void ScreenOnlyAgent_WorkingThenIdle_IsDone_AndGeminiAllowsWithEnter()
    {
        var node = new TerminalNodeViewModel { Title = "Gemini", CommandLine = "gemini" };
        Assert.Equal("\r", node.AllowKey);
        node.ApplyScreen(ScreenRules.ToStatus(Rules.Match("gemini", Fixture("gemini-working"))));
        Assert.Equal(NodeStatus.Working, node.Status);
        node.ApplyScreen(ScreenRules.ToStatus(Rules.Match("gemini", Fixture("gemini-idle"))));
        Assert.Equal(NodeStatus.Done, node.Status);
        node.ApplyScreen(ScreenRules.ToStatus(Rules.Match("gemini", Fixture("gemini-idle"))));
        Assert.Equal(NodeStatus.Done, node.Status);

        node.ApplyScreen(ScreenRules.ToStatus(Rules.Match("gemini", Fixture("gemini-permission"))));
        Assert.Equal(NodeStatus.Waiting, node.Status);
        Assert.True(node.AwaitsPermission);
    }

    [Fact]
    public void ClaudeTrustPrompt_WaitsWithoutAllowDeny()
    {
        var node = new TerminalNodeViewModel { Title = "Claude", CommandLine = "cmd.exe /k \"C:\\npm\\claude.cmd\"" };
        node.ApplyScreen(ScreenRules.ToStatus(Rules.Match(node.AgentKind, Fixture("claude-trust"))));
        Assert.Equal(NodeStatus.Waiting, node.Status);
        Assert.Equal("Needs you · Trust this folder?", node.StatusText);
        Assert.False(node.AwaitsPermission); // its first option is "No, exit": Allow must not press it

        // Once trusted, the hooks take over
        node.ApplyScreen(null);
        node.ApplyAgentEvent(new AgentEvent("UserPromptSubmit"));
        Assert.Equal(NodeStatus.Working, node.Status);
    }
}
