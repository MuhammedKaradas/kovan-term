using System.IO;
using System.Text.Json;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.Services.Mesh;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Board tasks the user gives to an agent: they follow the agent's status (#16).</summary>
public sealed class BoardAssignTests : IDisposable
{
    private static readonly Guid Agent = Guid.NewGuid(), Other = Guid.NewGuid();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public BoardAssignTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Assigned_StaysToDoUntilTheAgentWorks_ThenDoneWhenItsTurnEnds()
    {
        var board = new MeshBoard();
        var task = board.CreateTask("Fix the login", null);

        board.AssignTask(task.Id, Agent, "Claude (aaaa)");
        Assert.Equal(("todo", Agent, true), (board.Tasks[0].State, board.Tasks[0].Owner, board.Tasks[0].Assigned));

        board.AgentFinished(Agent); // a turn that was already running ends: not this task's
        Assert.Equal("todo", board.Tasks[0].State);
        board.AgentStarted(Other);
        Assert.Equal("todo", board.Tasks[0].State);

        board.AgentStarted(Agent);
        Assert.Equal("doing", board.Tasks[0].State);
        board.AgentFinished(Agent);
        Assert.Equal("done", board.Tasks[0].State);
    }

    [Fact]
    public void ClaimedByTheAgentItself_IsNotFinishedByKovan()
    {
        var board = new MeshBoard();
        var task = board.CreateTask("Write tests", null);
        board.ClaimTask(task.Id, Agent, "Claude (aaaa)");

        board.AgentFinished(Agent);

        Assert.Equal("doing", board.Tasks[0].State);
    }

    [Fact]
    public void MovedBackToToDo_IsNobodysAndStopsFollowing()
    {
        var board = new MeshBoard();
        var task = board.CreateTask("Refactor", null);
        board.AssignTask(task.Id, Agent, "Claude (aaaa)");
        board.AgentStarted(Agent);

        board.MoveTask(task.Id, "todo");
        board.AgentStarted(Agent);

        Assert.Equal(("todo", (Guid?)null, false), (board.Tasks[0].State, board.Tasks[0].Owner, board.Tasks[0].Assigned));
    }

    [Fact]
    public void AssigningADoneTask_StartsItOver()
    {
        var board = new MeshBoard();
        var task = board.CreateTask("Ship", null);
        board.CompleteTask(task.Id, Other, "Codex (bbbb)", "pushed");

        board.AssignTask(task.Id, Agent, "Claude (aaaa)");

        Assert.Equal(("todo", Agent, (string?)null), (board.Tasks[0].State, board.Tasks[0].Owner, board.Tasks[0].Note));
    }

    [Fact]
    public void SavedBoardFromBeforeAssigning_Loads()
    {
        var json = """{"tasks":[{"id":1,"title":"Old","detail":null,"state":"doing","owner":null,"ownerName":null,"note":null,"updated":"2026-11-01T10:00:00+03:00"}],"messages":[]}""";
        var state = JsonSerializer.Deserialize<MeshBoardState>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

        Assert.False(state.Tasks[0].Assigned);
        Assert.Equal("doing", state.Tasks[0].State);
    }

    [Fact]
    public void Card_SaysWhatHappensNext()
    {
        var board = new MeshBoard();
        var task = board.CreateTask("Docs", null);
        Assert.Null(new TaskCard(board.Tasks[0]).Owner);
        Assert.True(new TaskCard(board.Tasks[0]).CanAssign);

        board.AssignTask(task.Id, Agent, "Claude (aaaa)");
        Assert.StartsWith("Given to Claude (aaaa)", new TaskCard(board.Tasks[0]).Owner);
        board.AgentStarted(Agent);
        Assert.False(new TaskCard(board.Tasks[0]).CanAssign);
        board.AgentFinished(Agent);
        Assert.Equal("Claude (aaaa)", new TaskCard(board.Tasks[0]).Owner);
    }

    [Fact]
    public void DropOnAColumn_MovesTheCard()
    {
        var board = new MeshBoard();
        board.CreateTask("Drag me", null);
        var panel = new BoardViewModel(board);
        panel.Refresh();

        panel.Drop(panel.Todo[0], "done");
        panel.Refresh();

        Assert.Empty(panel.Todo);
        Assert.Single(panel.Done);
    }

    [Fact]
    public void TaskPrompt_IncludesTheDetail()
    {
        var task = new MeshTask(3, "Fix the login", "Users get a 500 after reset", "todo", null, null, null, DateTimeOffset.Now);

        Assert.Equal("Task #3 from the Kovan board: Fix the login\n\nUsers get a 500 after reset", MainViewModel.TaskPrompt(task));
    }

    // Through the real status path: a Codex node reporting its state with OSC 7501
    [Fact]
    public void AgentStatusReports_AdvanceTheAssignedCard()
    {
        File.WriteAllText(Path.Combine(_dir, "codex.exe"), "");
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = [new NodeState { Title = "Codex", CommandLine = "codex", Width = 500, Height = 300 }] });
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        var vm = new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), new SettingsStore(Path.Combine(_dir, "settings.json")));
        var codex = Assert.Single(vm.TaskAgents());
        string? typed = null;
        vm.TerminalInputRequested += (_, text) => typed = text;
        vm.MeshBoard.CreateTask("Add a README", null);
        vm.BoardPanel.Refresh();

        vm.AssignTask(vm.BoardPanel.Todo[0], codex);
        Assert.Contains("Task #1 from the Kovan board: Add a README", typed);

        vm.OnProgramStatus(codex, [new ProgramStatusReport("working")]);
        Assert.Equal("doing", vm.MeshBoard.Tasks[0].State);
        vm.OnProgramStatus(codex, [new ProgramStatusReport("blocked", Kind: "permission")]);
        Assert.Equal("doing", vm.MeshBoard.Tasks[0].State); // a question isn't the end of the turn
        vm.OnProgramStatus(codex, [new ProgramStatusReport("working")]);
        vm.OnProgramStatus(codex, [new ProgramStatusReport("done")]);
        Assert.Equal("done", vm.MeshBoard.Tasks[0].State);
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => false;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string action) => Task.FromResult(false);
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(null);
    }
}
