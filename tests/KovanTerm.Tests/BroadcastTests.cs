using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>One prompt sent to several agents (#78).</summary>
public sealed class BroadcastTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public BroadcastTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class Dialogs(Func<BroadcastViewModel, bool> answer) : IDialogService
    {
        public List<string> Messages { get; } = [];
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => false;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmAsync(string title, string message, string action) => Task.FromResult(true);
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(null);
        public bool ShowBroadcast(BroadcastViewModel viewModel) => answer(viewModel);
    }

    private static readonly Guid W1 = Guid.NewGuid(), W2 = Guid.NewGuid();

    // Terminals need their program on PATH: the commands are .exe files in the test folder
    private (MainViewModel Vm, List<(string Node, string Text)> Sent) Create(Dialogs dialogs, params NodeState[] nodes)
    {
        foreach (var exe in new[] { "claude.exe", "codex.exe", "cmd.exe" }) File.WriteAllText(Path.Combine(_dir, exe), "");
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState
        {
            Workspaces = [new WorkspaceInfo { Id = W1, Name = "One" }, new WorkspaceInfo { Id = W2, Name = "Two" }],
            ActiveWorkspaceId = W1,
            Nodes = nodes.ToList(),
        });
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        var vm = new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), dialogs, new SettingsStore(Path.Combine(_dir, "settings.json")));
        var sent = new List<(string, string)>();
        vm.TerminalInputRequested += (node, text) => sent.Add((node.Title, text));
        return (vm, sent);
    }

    private static NodeState Node(string title, string command, Guid workspace, double x = 0) =>
        new() { Title = title, CommandLine = command, WorkspaceId = workspace, X = x, Width = 400, Height = 300 };

    private static readonly NodeState[] Canvas =
    [
        Node("Claude", "claude.exe", W1),
        Node("Codex", "codex.exe", W1, x: 500),
        Node("Shell", "cmd.exe", W1, x: 1000),
        Node("Plan", "", W1, x: 1500) with { Note = "a note" },
        Node("Claude 2", "claude.exe", W2),
    ];

    [Fact]
    public void ByDefault_ThisWorkspacesAgentsAreChecked_AndNotesArentOffered()
    {
        var (vm, _) = Create(new Dialogs(_ => false), Canvas);

        var dialog = vm.CreateBroadcast();

        Assert.Equal(["Claude", "Codex", "Shell", "Claude 2"], dialog.Targets.Select(t => t.Title));
        Assert.Equal(["Claude", "Codex"], dialog.Checked.Select(n => n.Title));
        Assert.Contains("Two", dialog.Targets[3].Detail);
    }

    [Fact]
    public void SelectedNodes_AreTheOnesChecked()
    {
        var (vm, _) = Create(new Dialogs(_ => false), Canvas);
        var shell = vm.Nodes.Single(n => n.Title == "Shell");
        var note = vm.Nodes.Single(n => n.Title == "Plan");

        var dialog = vm.CreateBroadcast([shell, note]);

        Assert.Equal(["Shell"], dialog.Checked.Select(n => n.Title));
    }

    [Fact]
    public async Task Send_PastesIntoEachThenPressesEnter()
    {
        var (vm, sent) = Create(new Dialogs(d =>
        {
            d.Text = "  Run the tests\nand fix what fails  ";
            d.Targets.Single(t => t.Title == "Claude 2").IsChecked = true;
            return true;
        }), Canvas);

        Assert.Equal(3, await vm.BroadcastAsync());

        var paste = "\x1b[200~Run the tests\nand fix what fails\x1b[201~";
        Assert.Equal([("Claude", paste), ("Codex", paste), ("Claude 2", paste), ("Claude", "\r"), ("Codex", "\r"), ("Claude 2", "\r")], sent);
    }

    [Fact]
    public async Task WithoutEnter_ItWaitsInEachTerminal()
    {
        var (vm, sent) = Create(new Dialogs(d =>
        {
            d.Text = "review";
            d.PressEnter = false;
            return true;
        }), Canvas);

        await vm.BroadcastAsync();

        Assert.DoesNotContain(sent, s => s.Text == "\r");
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public async Task Cancelled_SendsNothing()
    {
        var (vm, sent) = Create(new Dialogs(d =>
        {
            d.Text = "x";
            return false;
        }), Canvas);

        Assert.Equal(0, await vm.BroadcastAsync());
        Assert.Empty(sent);
    }

    [Fact]
    public async Task NoTerminals_SaysSo()
    {
        var dialogs = new Dialogs(_ => true);
        var (vm, _) = Create(dialogs, Node("Plan", "", W1) with { Note = "only a note" });

        Assert.Equal(0, await vm.BroadcastAsync());
        Assert.Equal(["There's no terminal to send to."], dialogs.Messages);
    }

    [Fact]
    public void Send_NeedsTextAndSomeoneToSendTo()
    {
        var (vm, _) = Create(new Dialogs(_ => false), Canvas);
        var dialog = vm.CreateBroadcast();

        Assert.False(dialog.SendCommand.CanExecute(null));
        dialog.Text = "  ";
        Assert.False(dialog.SendCommand.CanExecute(null));
        dialog.Text = "go";
        Assert.True(dialog.SendCommand.CanExecute(null));
        Assert.Equal("Send to 2", dialog.SendText);

        dialog.CheckAllCommand.Execute(null);
        Assert.Equal("Send to 4", dialog.SendText);
        dialog.CheckAllCommand.Execute(null);
        Assert.False(dialog.SendCommand.CanExecute(null));
    }
}
