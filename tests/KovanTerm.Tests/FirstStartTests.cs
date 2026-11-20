using System.IO;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>The first start: a shell and a welcome note, no agent in the home folder; the dialog suggests an installed agent.</summary>
public sealed class FirstStartTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public FirstStartTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class Dialogs : IDialogService
    {
        public NewTerminalViewModel? Shown { get; private set; }
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) { Shown = viewModel; return false; }
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string action) => Task.FromResult(false);
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(null);
    }

    /// <summary>A first start on a computer with these programs (empty .exe stand-ins on the PATH).</summary>
    private (MainViewModel Vm, Dialogs Dialogs) FirstStart(params string[] programs)
    {
        var bin = Directory.CreateDirectory(Path.Combine(_dir, "bin")).FullName;
        foreach (var p in programs) File.WriteAllText(Path.Combine(bin, p + ".exe"), "");
        var resolver = new CommandResolver(path: bin, pathExt: ".exe");
        var dialogs = new Dialogs();
        var vm = new MainViewModel(new WorkspaceStore(Path.Combine(_dir, "workspace.json")), new ProfileStore(Path.Combine(_dir, "profiles.json")),
            resolver, new WslLauncher(resolver, () => ""), dialogs, new SettingsStore(Path.Combine(_dir, "settings.json")));
        return (vm, dialogs);
    }

    [Fact]
    public void No_agent_starts_in_the_home_folder()
    {
        var (vm, _) = FirstStart("powershell", "claude", "codex");

        Assert.Equal(["PowerShell", "Welcome"], vm.Nodes.Select(n => n.Title));
        Assert.All(vm.Nodes, n => Assert.True(string.IsNullOrEmpty(n.AgentKind)));
        Assert.Contains("**Claude Code**, **Codex**", vm.Nodes.Single(n => n.IsNote).NoteText);
    }

    [Fact]
    public async Task New_terminal_suggests_the_agent_that_is_installed()
    {
        var (vm, dialogs) = FirstStart("powershell", "codex", "agy");

        await vm.NewTerminalCommand.ExecuteAsync(null);

        Assert.Equal("codex", dialogs.Shown!.SelectedProfile!.Profile.Id);
    }

    [Fact]
    public void Without_an_agent_the_note_says_how_to_get_one()
    {
        var (vm, _) = FirstStart("powershell");

        Assert.Contains("No coding agent was found", vm.Nodes.Single(n => n.IsNote).NoteText);
    }

    [Fact]
    public void Agents_are_suggested_in_a_fixed_order()
    {
        Assert.Equal("claude", MainViewModel.AgentPreference[0]);
        Assert.All(MainViewModel.AgentPreference, id => Assert.Contains(ProfileStore.Defaults, p => p.Id == id));
    }
}
