using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class ClaudeSessionsTests : IDisposable
{
    private readonly string _projects = Path.Combine(Path.GetTempPath(), "kovan-sessions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_projects)) Directory.Delete(_projects, recursive: true);
    }

    [Fact]
    public void NewConversation_GetsTheNodesId_SavedOneIsResumed()
    {
        var sessions = new ClaudeSessions(_projects);
        var id = Guid.NewGuid().ToString();
        Assert.Equal($"--session-id {id}", sessions.Arguments(id, ""));

        // Claude saves transcripts under a folder named after the project path
        var folder = Directory.CreateDirectory(Path.Combine(_projects, "C--work-app"));
        File.WriteAllText(Path.Combine(folder.FullName, id + ".jsonl"), "{}");
        Assert.True(sessions.Exists(id));
        Assert.Equal($"--resume {id}", sessions.Arguments(id, "--model opus"));
    }

    [Theory]
    [InlineData("--resume abc", true)]
    [InlineData("-r", true)]
    [InlineData("--continue", true)]
    [InlineData("--model opus -c", true)]
    [InlineData("--session-id=x", true)]
    [InlineData("--model opus", false)]
    [InlineData("--chrome", false)]
    [InlineData("", false)]
    public void ProfileThatChoosesItsConversation_IsLeftAlone(string args, bool chooses)
    {
        Assert.Equal(chooses, ClaudeSessions.ChoosesConversation(args));
        if (chooses) Assert.Equal("", new ClaudeSessions(_projects).Arguments("id", args));
    }

    [Fact]
    public void MissingProjectsFolder_MeansNotSaved() =>
        Assert.False(new ClaudeSessions(Path.Combine(_projects, "nope")).Exists("x"));

    [Fact]
    public void SessionId_FollowsEvents_AndIsPersisted()
    {
        var node = TerminalNodeViewModel.FromState(new NodeState { Title = "c", CommandLine = "claude", SessionId = "first" });
        Assert.Equal("first", node.SessionId);

        // After /clear the agent reports a new conversation
        node.ApplyAgentEvent(new AgentEvent("UserPromptSubmit", SessionId: "second"));
        Assert.Equal("second", node.ToState().SessionId);
    }
}
