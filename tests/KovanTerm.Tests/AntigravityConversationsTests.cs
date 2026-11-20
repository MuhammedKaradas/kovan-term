using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>An Antigravity node keeps its conversation: read from Antigravity's folder map after a turn, resumed on start.</summary>
public sealed class AntigravityConversationsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private string Home => Path.Combine(_dir, "agy-home");
    private const string Id = "bf70cffb-df88-4a80-962c-474a99e6dd32";
    private const string Other = "6588a93f-2973-433b-a861-779498c2c4d7";

    public AntigravityConversationsTests()
    {
        Directory.CreateDirectory(Path.Combine(Home, "cache"));
        Directory.CreateDirectory(Path.Combine(Home, "conversations"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Map(string json) => File.WriteAllText(Path.Combine(Home, "cache", "last_conversations.json"), json);
    private void Saved(string id) => File.WriteAllText(Path.Combine(Home, "conversations", id + ".db"), "");

    [Fact]
    public void The_folders_latest_conversation_is_found_whatever_the_slashes_and_case()
    {
        Map($$"""{ "F:\\kovan-demo\\docs": "{{Id}}", "F:\\other": "{{Other}}" }""");
        var c = new AntigravityConversations(Home);

        Assert.Equal(Id, c.Latest(@"F:\kovan-demo\docs"));
        Assert.Equal(Id, c.Latest(@"f:\Kovan-Demo\docs\"));
        Assert.Equal(Id, c.Latest("F:/kovan-demo/docs"));
        Assert.Null(c.Latest(@"F:\nowhere"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "F:\\kovan-demo\\docs": "not-an-id" }""")]
    public void A_bad_map_gives_nothing(string json)
    {
        Map(json);
        Assert.Null(new AntigravityConversations(Home).Latest(@"F:\kovan-demo\docs"));
    }

    [Fact]
    public void Only_a_saved_conversation_is_resumed_and_a_profile_that_picks_one_is_left_alone()
    {
        var c = new AntigravityConversations(Home);
        Assert.Equal("", c.Arguments(Id, ""));

        Saved(Id);
        Assert.Equal($"--conversation {Id}", c.Arguments(Id, ""));
        Assert.Equal("", c.Arguments(Id, "-c"));
        Assert.Equal("", c.Arguments(Id, $"--conversation={Other}"));
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

    private MainViewModel Restore(params NodeState[] nodes)
    {
        var bin = Directory.CreateDirectory(Path.Combine(_dir, "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, "agy.exe"), "");
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = nodes.ToList() });
        var resolver = new CommandResolver(path: bin, pathExt: ".exe");
        var vm = new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), new SettingsStore(Path.Combine(_dir, "settings.json")),
            antigravity: new AntigravityConversations(Home));
        return vm;
    }

    private NodeState Agy(string folder, string? id = null) => new()
    {
        Id = Guid.NewGuid(), ProfileId = "antigravity", Title = "Antigravity", CommandLine = "agy",
        WorkingDirectory = folder, SessionId = id, Width = 400, Height = 300,
    };

    private static readonly IReadOnlyList<ProgramStatusReport> Done = [new("done", App: "agy")];

    [Fact]
    public void After_a_turn_the_node_takes_its_folders_conversation()
    {
        Map($$"""{ "{{_dir.Replace("\\", "\\\\")}}": "{{Id}}" }""");
        var vm = Restore(Agy(_dir));
        var node = vm.Nodes.Single();

        vm.OnProgramStatus(node, Done);

        Assert.Equal(Id, node.SessionId);
        Assert.Equal(Id, node.ToState().SessionId);
    }

    [Fact]
    public void Two_nodes_in_one_folder_take_none()
    {
        Map($$"""{ "{{_dir.Replace("\\", "\\\\")}}": "{{Id}}" }""");
        var vm = Restore(Agy(_dir), Agy(_dir));

        vm.OnProgramStatus(vm.Nodes[0], Done);

        Assert.All(vm.Nodes, n => Assert.Null(n.SessionId));
    }

    [Fact]
    public void A_restored_node_resumes_its_conversation()
    {
        Saved(Id);
        var node = Restore(Agy(_dir, Id)).Nodes.Single();

        Assert.Contains($"--conversation {Id}", node.StartupCommandLine);
    }
}
