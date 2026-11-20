using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Qwen Code and OpenCode nodes: agents, their status from OSC 7501, and Qwen's conversation.</summary>
public sealed class QwenOpenCodeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public QwenOpenCodeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\npm\qwen.cmd", "qwen")]
    [InlineData(@"C:\Users\me\AppData\Roaming\npm\opencode.cmd", "opencode")]
    public void BothAreAgents(string path, string kind) => Assert.Equal(kind, ScreenRules.AgentOfExecutable(path));

    [Fact]
    public void ProfilesAreThere() =>
        Assert.Equal(["opencode", "qwen"], ProfileStore.Defaults.Where(p => p.Id is "opencode" or "qwen").Select(p => p.Executable));

    [Theory]
    [InlineData("qwen", "1")]
    [InlineData("opencode", "\r")]
    public void Allow_presses_the_agents_yes_key(string exe, string key) =>
        Assert.Equal(key, new TerminalNodeViewModel { Title = exe, CommandLine = exe }.AllowKey);

    [Fact]
    public void Qwens_permission_report_makes_the_node_wait_for_Allow()
    {
        // Qwen Code 0.28 ends its reports with ST and base64-encodes the message
        var msg = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Permission needed: run_shell_command"));
        var reports = new ProgramStatusParser().Feed($"\x1b]7501;state=blocked:kind=permission:app=qwen-code:msg={msg}\x1b\\");
        var node = new TerminalNodeViewModel { Title = "Qwen", CommandLine = "qwen" };

        node.ApplyProgramStatus(Assert.Single(reports));

        Assert.Equal(NodeStatus.Waiting, node.Status);
        Assert.True(node.AwaitsPermission);
        Assert.Contains("run_shell_command", node.StatusText);
    }

    [Fact]
    public void A_new_conversation_gets_the_nodes_id_and_a_saved_one_is_resumed()
    {
        var sessions = new QwenSessions(_dir);
        const string id = "4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55";

        Assert.Equal($"--session-id {id}", sessions.Arguments(id, ""));

        var chats = Directory.CreateDirectory(Path.Combine(_dir, "F--work-shop", "chats"));
        File.WriteAllText(Path.Combine(chats.FullName, id + ".jsonl"), "{}");
        Assert.Equal($"--resume {id}", sessions.Arguments(id, "--yolo"));
    }

    [Theory]
    [InlineData("-c")]
    [InlineData("--continue")]
    [InlineData("--resume 1234")]
    [InlineData("--session-id=1234")]
    public void A_profile_that_picks_its_conversation_is_left_alone(string arguments) =>
        Assert.Equal("", new QwenSessions(_dir).Arguments("4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55", arguments));

    [Fact]
    public void A_restored_Qwen_node_keeps_its_conversation()
    {
        File.WriteAllText(Path.Combine(_dir, "qwen.cmd"), "@echo should-not-run");
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState
        {
            Nodes = [new() { ProfileId = "qwen", Title = "Qwen", CommandLine = "qwen", SessionId = "4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55", Width = 400, Height = 300 }],
        });
        var resolver = new CommandResolver(path: _dir, pathExt: ".cmd;.exe");
        var vm = new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), new SettingsStore(Path.Combine(_dir, "settings.json")));

        var node = vm.Nodes.Single();

        Assert.Equal("4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55", node.SessionId);
        Assert.Matches("--(session-id|resume) 4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55", node.StartupCommandLine);
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
