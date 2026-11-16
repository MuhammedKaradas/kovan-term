using System.Net.Http;
using System.Text;
using System.Text.Json;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.Tests;

public sealed class AgentStatusTests
{
    private static AgentStatus Run(params AgentEvent[] events) =>
        events.Aggregate(AgentStatus.Idle, AgentStatusReducer.Apply);

    [Fact]
    public void ToolUse_IsWorkingWithTheToolName()
    {
        var s = Run(new("UserPromptSubmit"), new("PreToolUse", ToolName: "Bash"));
        Assert.Equal(NodeStatus.Working, s.Status);
        Assert.Equal("Working · Bash", AgentStatusReducer.Describe(s));
    }

    [Fact]
    public void PermissionRequest_WaitsAndCanBeAnswered()
    {
        var s = Run(new("PreToolUse", ToolName: "Bash"), new("PermissionRequest", ToolName: "Bash"));
        Assert.Equal(NodeStatus.Waiting, s.Status);
        Assert.True(s.AwaitsPermission);
        Assert.Equal("Needs you · Bash", AgentStatusReducer.Describe(s));
    }

    [Fact]
    public void PermissionNotification_KeepsTheToolFromPermissionRequest()
    {
        var s = Run(new("PermissionRequest", ToolName: "Edit"), new("Notification", NotificationType: "permission_prompt"));
        Assert.Equal("Needs you · Edit", AgentStatusReducer.Describe(s));
        Assert.True(s.AwaitsPermission);
    }

    [Theory]
    [InlineData("AskUserQuestion")]
    [InlineData("ExitPlanMode")]
    public void QuestionsAndPlans_WaitWithoutAllowDeny(string tool)
    {
        var s = Run(new AgentEvent("PermissionRequest", ToolName: tool));
        Assert.Equal(NodeStatus.Waiting, s.Status);
        Assert.False(s.AwaitsPermission);
    }

    [Fact]
    public void ApprovedTool_GoesBackToWorking_ThenStopIsDone()
    {
        var s = Run(new("PermissionRequest", ToolName: "Bash"), new("PostToolUse", ToolName: "Bash"));
        Assert.Equal(NodeStatus.Working, s.Status);
        Assert.False(s.AwaitsPermission);
        Assert.Equal(NodeStatus.Done, Run(new("PostToolUse"), new("Stop")).Status);
        Assert.Equal(NodeStatus.Error, Run(new AgentEvent("StopFailure")).Status);
    }

    [Fact]
    public void BackgroundSubagentAfterStop_StaysDone()
    {
        // Seen with a resumed session: a background subagent finishes after the main agent's Stop
        var s = Run(new("UserPromptSubmit"), new("Stop"), new("SubagentStop"));
        Assert.Equal(NodeStatus.Done, s.Status);
        Assert.Equal(NodeStatus.Working, Run(new("UserPromptSubmit"), new("SubagentStart")).Status);
    }

    [Fact]
    public void IdleNotification_DoesNotChangeStatus()
    {
        var s = Run(new("Stop"), new("Notification", NotificationType: "idle_prompt"));
        Assert.Equal(NodeStatus.Done, s.Status);
    }

    [Fact]
    public void ParseEvent_ReadsHookInput()
    {
        var json = """{"hook_event_name":"PermissionRequest","session_id":"abc","tool_name":"Bash","tool_input":{"command":"ls"}}""";
        Assert.Equal(new AgentEvent("PermissionRequest", "abc", "Bash"), AgentHookServer.ParseEvent(Encoding.UTF8.GetBytes(json)));
        Assert.Null(AgentHookServer.ParseEvent("not json"u8));
        Assert.Null(AgentHookServer.ParseEvent("""{"session_id":"abc"}"""u8));
    }

    [Fact]
    public void ParsePath_AcceptsOnlyHookUrls()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, AgentHookServer.ParsePath($"/hook/{id}"));
        Assert.Null(AgentHookServer.ParsePath("/hook/nope"));
        Assert.Null(AgentHookServer.ParsePath($"/other/{id}"));
    }

    [Fact]
    public async Task Server_RaisesEventsOnlyWithTheToken()
    {
        using var server = new AgentHookServer();
        var received = new TaskCompletionSource<(Guid, AgentEvent)>();
        server.EventReceived += (id, e) => received.TrySetResult((id, e));
        var nodeId = Guid.NewGuid();
        using var http = new HttpClient();
        var body = new StringContent("""{"hook_event_name":"Stop","session_id":"s1"}""", Encoding.UTF8, "application/json");

        var denied = await http.PostAsync(server.UrlFor(nodeId), body);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, server.UrlFor(nodeId)) { Content = body };
        request.Headers.Add(AgentHookServer.TokenHeader, server.Token);
        var ok = await http.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);

        var (gotId, gotEvent) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(nodeId, gotId);
        Assert.Equal(new AgentEvent("Stop", "s1"), gotEvent);
    }

    [Fact]
    public void Settings_HaveAnHttpHookForEveryEvent()
    {
        using var doc = JsonDocument.Parse(ClaudeHooks.BuildSettings("http://127.0.0.1:1/hook/x", "tok"));
        var hooks = doc.RootElement.GetProperty("hooks");
        foreach (var name in ClaudeHooks.Events)
        {
            var hook = hooks.GetProperty(name)[0].GetProperty("hooks")[0];
            Assert.Equal("http", hook.GetProperty("type").GetString());
            Assert.Equal("tok", hook.GetProperty("headers").GetProperty(AgentHookServer.TokenHeader).GetString());
        }
    }

    [Theory]
    [InlineData("\x1b", true)]
    [InlineData("\r", true)]
    [InlineData("1", true)]
    [InlineData("\x1b[B", false)]          // arrow key
    [InlineData("a", false)]
    [InlineData("\x1b[0;1;13;1;0;1_", true)]  // win32-input-mode: Enter down
    [InlineData("\x1b[27;1;27;1;0;1_", true)] // Escape down
    [InlineData("\x1b[49;2;49;0;0;1_", false)] // '1' up
    [InlineData("\x1b[40;80;0;1;0;1_", false)] // arrow down
    public void PromptKeys_RecognizeAnswers(string input, bool expected) => Assert.Equal(expected, PromptKeys.IsAnswer(input));

    [Fact]
    public void AnsweredInTerminal_LeavesWaiting()
    {
        var node = new KovanTerm.ViewModels.TerminalNodeViewModel { Title = "c", CommandLine = "claude" };
        node.ApplyAgentEvent(new AgentEvent("PermissionRequest", ToolName: "Write"));
        Assert.Equal(NodeStatus.Waiting, node.Status);

        node.PromptAnswered(allowed: false);
        Assert.Equal(NodeStatus.Idle, node.Status);
        Assert.False(node.AwaitsPermission);
        Assert.Null(node.StatusText);

        // Only a waiting node is affected
        node.ApplyAgentEvent(new AgentEvent("PreToolUse", ToolName: "Bash"));
        node.PromptAnswered(allowed: false);
        Assert.Equal(NodeStatus.Working, node.Status);
    }

    [Theory]
    [InlineData(@"C:\Program Files\nodejs\claude.cmd", true)]
    [InlineData(@"C:\tools\Claude.exe", true)]
    [InlineData(@"C:\Windows\System32\cmd.exe", false)]
    [InlineData(@"C:\tools\claude-helper.exe", false)]
    public void IsClaude_ByFileName(string path, bool expected) => Assert.Equal(expected, ClaudeHooks.IsClaude(path));
}
