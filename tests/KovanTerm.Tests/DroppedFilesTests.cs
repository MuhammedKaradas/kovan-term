using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Files dropped on a terminal, typed in as paths.</summary>
public sealed class DroppedFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public DroppedFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Windows_QuotesOnlyWhatNeedsIt()
    {
        Assert.Equal(@"C:\shots\login.png ""C:\My Shots\a b.png"" ""C:\x\R&D (1).txt"" ",
            DroppedFiles.Format([@"C:\shots\login.png", @"C:\My Shots\a b.png", @"C:\x\R&D (1).txt"], wsl: false, distro: null));
    }

    [Fact]
    public void Wsl_GetsLinuxPaths_QuotedForTheShell()
    {
        Assert.Equal("/mnt/c/shots/login.png '/mnt/c/My Shots/it'\\''s $5!.png' /home/me/app/main.py ",
            DroppedFiles.Format([@"C:\shots\login.png", @"C:\My Shots\it's $5!.png", @"\\wsl.localhost\Debian\home\me\app\main.py"],
                wsl: true, distro: "Debian"));
    }

    [Fact]
    public void ControlCharacters_AreDropped_SoNothingRunsByItself()
    {
        Assert.Equal(@"C:\a\evil.txt ", DroppedFiles.Format(["C:\\a\\evil\n.txt"], wsl: false, distro: null));
        Assert.Equal("", DroppedFiles.Format(["\r\n"], wsl: false, distro: null));
    }

    [Fact]
    public void Dropped_IsPastedIntoThatTerminal_AndMakesItCurrent()
    {
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = [new NodeState { Title = "Shell", CommandLine = "cmd.exe", Width = 400, Height = 300 },
            new NodeState { Title = "Plan", CommandLine = "", Note = "x", X = 500, Width = 400, Height = 300 }] });
        File.WriteAllText(Path.Combine(_dir, "cmd.exe"), "");
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        var vm = new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), new SettingsStore(Path.Combine(_dir, "settings.json")));
        var sent = new List<(string, string)>();
        vm.TerminalInputRequested += (n, t) => sent.Add((n.Title, t));
        var shell = vm.Nodes.Single(n => n.Title == "Shell");

        vm.DropOnNode(shell, [@"C:\shots\login.png"], null);
        vm.DropOnNode(shell, null, "  some text  ");
        vm.DropOnNode(vm.Nodes.Single(n => n.Title == "Plan"), [@"C:\x.png"], null);

        Assert.Equal([("Shell", "\x1b[200~C:\\shots\\login.png \x1b[201~"), ("Shell", "\x1b[200~some text\x1b[201~")], sent);
        Assert.Same(shell, vm.CurrentNode);
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
