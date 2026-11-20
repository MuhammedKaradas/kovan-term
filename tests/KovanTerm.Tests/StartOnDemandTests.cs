using System.IO;
using System.Text.Json;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Agents that start on demand, showing their conversation until then (#102).</summary>
public sealed class StartOnDemandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public StartOnDemandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string Line(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public void Preview_IsWhatWasSaid_WithoutToolsOrReminders()
    {
        var lines = new[]
        {
            Line(new { type = "user", message = new { content = "Add a coupon field" } }),
            Line(new { type = "assistant", message = new { content = new object[] { new { type = "tool_use", name = "Read" } } } }),
            Line(new { type = "user", message = new { content = new object[] { new { type = "tool_result", content = "file" } } } }),
            Line(new { type = "user", isMeta = true, message = new { content = "Caveat: meta" } }),
            Line(new { type = "user", message = new { content = "<command-name>/clear</command-name>" } }),
            Line(new { type = "assistant", isSidechain = true, message = new { content = new object[] { new { type = "text", text = "subagent" } } } }),
            Line(new { type = "assistant", message = new { content = new object[] { new { type = "text", text = "Added it.\n\n\n\nUnder the summary." } } } }),
            "not json",
        };

        var preview = ConversationPreview.Parse(lines, 8);

        Assert.Equal([new PreviewMessage(true, "Add a coupon field"), new PreviewMessage(false, "Added it.\n\nUnder the summary.")], preview);
    }

    [Fact]
    public void Preview_KeepsTheLastMessages_AndShortensLongOnes()
    {
        var lines = Enumerable.Range(1, 12).Select(i => Line(new { type = "user", message = new { content = $"m{i}" } }))
            .Append(Line(new { type = "user", message = new { content = new string('x', 2000) } }));

        var preview = ConversationPreview.Parse(lines, 3);

        Assert.Equal(["m11", "m12"], preview.Take(2).Select(m => m.Text));
        Assert.EndsWith("…", preview[2].Text);
        Assert.True(preview[2].Text.Length <= 601);
    }

    private MainViewModel Restore(bool startAgents, params NodeState[] nodes)
    {
        foreach (var exe in new[] { "claude.exe", "cmd.exe" }) File.WriteAllText(Path.Combine(_dir, exe), "");
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = nodes.ToList() });
        var settings = new SettingsStore(Path.Combine(_dir, "settings.json"));
        settings.Save(new AppSettings { StartAgentsOnOpen = startAgents });
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        return new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), settings, sessions: new ClaudeSessions(Path.Combine(_dir, "projects")));
    }

    private static NodeState Node(string title, string command, double x = 0) =>
        new() { Title = title, CommandLine = command, X = x, Width = 400, Height = 300 };

    [Fact]
    public void Off_AgentsWait_ShellsStart()
    {
        var vm = Restore(startAgents: false, Node("Claude", "claude.exe"), Node("Shell", "cmd.exe", 500));

        Assert.True(vm.Nodes.Single(n => n.Title == "Claude").IsDormant);
        Assert.False(vm.Nodes.Single(n => n.Title == "Shell").IsDormant);
    }

    [Fact]
    public void On_AsBefore_EverythingStarts() =>
        Assert.All(Restore(startAgents: true, Node("Claude", "claude.exe")).Nodes, n => Assert.False(n.IsDormant));

    [Fact]
    public void Start_And_AMessage()
    {
        var vm = Restore(startAgents: false, Node("Claude", "claude.exe"), Node("Codex", "claude.exe", 500));
        var (claude, other) = (vm.Nodes[0], vm.Nodes[1]);

        vm.StartNodeCommand.Execute(claude);
        Assert.False(claude.IsDormant);
        Assert.Same(claude, vm.CurrentNode);

        vm.SendToDormant(other, "  Run the tests  ");
        Assert.False(other.IsDormant);
        Assert.Equal("Run the tests", other.PendingMessage);

        // Not waiting any more: nothing to send
        vm.SendToDormant(claude, "late");
        Assert.Null(claude.PendingMessage);
    }

    [Fact]
    public void TheSetting_IsInTheWindow()
    {
        var store = new SettingsStore(Path.Combine(_dir, "s.json"));
        var settings = new SettingsViewModel(store);
        Assert.True(settings.StartAgentsOnOpen);
        settings.StartAgentsOnOpen = false;
        Assert.False(store.Load().StartAgentsOnOpen);
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
