using System.IO;
using System.Text;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class AgentsPanelTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"kovan-transcript-{Guid.NewGuid():N}.jsonl");

    public void Dispose() => File.Delete(_file);

    private static string Assistant(long input, long cacheCreate, long cacheRead, bool sidechain = false) =>
        "{\"type\":\"assistant\",\"isSidechain\":" + (sidechain ? "true" : "false") +
        ",\"message\":{\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":" + input +
        ",\"cache_creation_input_tokens\":" + cacheCreate + ",\"cache_read_input_tokens\":" + cacheRead +
        ",\"output_tokens\":900}}}";

    private const string User = """{"type":"user","message":{"role":"user","content":"hi"}}""";

    [Fact]
    public void ContextOf_SumsInputAndCache_IgnoresOtherLines()
    {
        Assert.Equal(1_000 + 200 + 64_000, TranscriptUsage.ContextOf(Assistant(1_000, 200, 64_000)));
        Assert.Null(TranscriptUsage.ContextOf(User));
        Assert.Null(TranscriptUsage.ContextOf(Assistant(5, 5, 5, sidechain: true)));
        Assert.Null(TranscriptUsage.ContextOf("{\"type\":\"assistant\",\"usage\" broken"));
    }

    [Fact]
    public void Update_ReadsOnlyNewCompleteLines()
    {
        var usage = new TranscriptUsage();
        File.WriteAllText(_file, User + "\n" + Assistant(10, 0, 1000) + "\n");
        Assert.True(usage.Update(_file));
        Assert.Equal(1010, usage.ContextTokens);

        // A reply still being written (no newline yet) isn't read until it's complete
        File.AppendAllText(_file, Assistant(20, 0, 2000));
        Assert.False(usage.Update(_file));
        File.AppendAllText(_file, "\n");
        Assert.True(usage.Update(_file));
        Assert.Equal(2020, usage.ContextTokens);

        // Nothing new
        Assert.False(usage.Update(_file));
    }

    [Fact]
    public void Update_LargeTranscript_ReadsOnlyTheTail()
    {
        var sb = new StringBuilder();
        var filler = new string('x', 1000);
        while (sb.Length < TranscriptUsage.MaxFirstRead + 100_000)
            sb.Append($$$"""{"type":"user","message":{"content":"{{{filler}}}"}}""").Append('\n');
        sb.Append(Assistant(1, 2, 3)).Append('\n');
        File.WriteAllText(_file, sb.ToString());

        var usage = new TranscriptUsage();
        Assert.True(usage.Update(_file));
        Assert.Equal(6, usage.ContextTokens);
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(12_345, "12.3k")]
    [InlineData(398_909, "398k")]
    [InlineData(1_250_000, "1.3M")]
    public void Format_IsShortAndCultureIndependent(long tokens, string expected) =>
        Assert.Equal(expected, TranscriptUsage.Format(tokens));

    [Fact]
    public void Subagents_AreTrackedFromEvents()
    {
        var node = new TerminalNodeViewModel { Title = "c", CommandLine = "claude" };
        node.ApplyAgentEvent(new AgentEvent("SubagentStart", AgentId: "a1", AgentType: "Explore"));
        node.ApplyAgentEvent(new AgentEvent("SubagentStart", AgentId: "a2", AgentType: "Explore"));
        node.ApplyAgentEvent(new AgentEvent("SubagentStart", AgentId: "a3", AgentType: "Plan"));
        Assert.Equal("3 subagents: Explore ×2, Plan", node.SubagentsText);

        node.ApplyAgentEvent(new AgentEvent("SubagentStop", AgentId: "a1", AgentType: "Explore"));
        node.ApplyAgentEvent(new AgentEvent("SubagentStop", AgentId: "a3", AgentType: "Plan"));
        Assert.Equal("1 subagent: Explore", node.SubagentsText);

        node.ApplyAgentEvent(new AgentEvent("SubagentStop", AgentId: "a2"));
        Assert.Null(node.SubagentsText);
    }

    [Fact]
    public void Panel_GroupsByFolder_AndPutsWaitingFirst()
    {
        var node = new TerminalNodeViewModel { Title = "c", CommandLine = "claude", WorkingDirectory = @"C:\work\shop-api\" };
        Assert.Equal("shop-api", node.GroupName);
        Assert.Equal(@"C:\", new TerminalNodeViewModel { Title = "c", CommandLine = "x", WorkingDirectory = @"C:\" }.GroupName);

        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal(3, node.StatusOrder);
        node.ApplyAgentEvent(new AgentEvent("PermissionRequest", ToolName: "Bash"));
        Assert.Equal(0, node.StatusOrder);
        Assert.Contains(nameof(TerminalNodeViewModel.StatusOrder), changed);

        node.SetContextTokens(65_400);
        Assert.Equal("ctx 65.4k", node.ContextText);
    }

    [Fact]
    public void ParseEvent_ReadsTranscriptAndSubagentFields()
    {
        var json = """{"hook_event_name":"SubagentStart","session_id":"s","transcript_path":"C:\\t.jsonl","agent_id":"a1","agent_type":"Explore"}""";
        Assert.Equal(new AgentEvent("SubagentStart", "s", TranscriptPath: @"C:\t.jsonl", AgentId: "a1", AgentType: "Explore"),
            AgentHookServer.ParseEvent(Encoding.UTF8.GetBytes(json)));
    }
}
