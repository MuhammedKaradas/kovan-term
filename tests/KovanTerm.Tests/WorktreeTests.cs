using System.Diagnostics;
using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class WorktreeTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "kovan-wt-" + Guid.NewGuid().ToString("N"));
    private string Repo => Path.Combine(_base, "shop");

    public void Dispose()
    {
        if (!Directory.Exists(_base)) return;
        foreach (var f in Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_base, recursive: true);
    }

    [Theory]
    [InlineData("kovan/claude-3f2a", true)]
    [InlineData("feature/login_v2.1", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("a..b", false)]
    [InlineData("/lead", false)]
    [InlineData("trail/", false)]
    [InlineData("x.lock", false)]
    [InlineData("a//b", false)]
    [InlineData("ref@{1}", false)]
    public void BranchNames_AreValidated(string name, bool valid) => Assert.Equal(valid, GitWorktrees.IsValidBranch(name));

    [Fact]
    public void Paths_AndSuggestions()
    {
        Assert.Equal(@"C:\src\shop.worktrees\kovan-claude-3f2a", GitWorktrees.PathFor(@"C:\src\shop\", "kovan/claude-3f2a"));
        var suggested = GitWorktrees.SuggestBranch("claude");
        Assert.Matches("^kovan/claude-[0-9a-f]{4}$", suggested);
        Assert.True(GitWorktrees.IsValidBranch(suggested));
    }

    [Fact]
    public void ParseNameStatus_ReadsRenames()
    {
        Assert.Equal(
            [new ChangedFile("a.cs", "M"), new ChangedFile("new.cs", "R", "old.cs"), new ChangedFile("gone.cs", "D")],
            GitChanges.ParseNameStatus("M\0a.cs\0R100\0old.cs\0new.cs\0D\0gone.cs\0"));
    }

    [Fact]
    public void Dialog_OffersWorktreeOnlyInsideARepository()
    {
        CreateRepo();
        var dialogs = new NoDialogs();
        ProfileOptionViewModel claude = new(new TerminalProfile { Id = "claude", Name = "Claude", Executable = "claude" }, @"C:\bin\claude.exe");

        var vm = new NewTerminalViewModel(dialogs, [claude], [Path.Combine(Repo, "src")], "claude");
        Assert.True(vm.CanUseWorktree);
        Assert.Equal(Repo, vm.RepoRoot);
        Assert.StartsWith("kovan/claude-", vm.BranchName);

        vm.UseWorktree = true;
        Assert.True(vm.CreatesWorktree);
        Assert.Equal(GitWorktrees.PathFor(Repo, vm.BranchName), vm.WorktreePathPreview);
        vm.BranchName = "bad name";
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        vm.BranchName = "agent/one";
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.WorkingDirectory = _base; // not a repository
        Assert.False(vm.CanUseWorktree);
        Assert.False(vm.CreatesWorktree);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Worktree_IsCreated_TracksWork_AndIsRemovedWithItsBranch()
    {
        CreateRepo();
        var created = await GitWorktrees.CreateAsync(Repo, "kovan/test-1");
        Assert.Null(created.Error);
        var w = created.Value!;
        Assert.Equal(GitWorktrees.PathFor(Repo, "kovan/test-1"), w.Path);
        Assert.True(File.Exists(Path.Combine(w.Path, "src", "app.cs")));
        Assert.Equal(new WorktreeState(0, 0), (await GitWorktrees.StateAsync(w)).Value);

        // The agent commits one change and leaves another uncommitted, plus a new file
        File.WriteAllText(Path.Combine(w.Path, "src", "app.cs"), "int a = 1;\n");
        Git(w.Path, "commit", "-q", "-am", "agent change");
        File.WriteAllText(Path.Combine(w.Path, "notes.md"), "todo\n");
        Assert.Equal(new WorktreeState(1, 1), (await GitWorktrees.StateAsync(w)).Value);

        // The Changes panel compares with the commit the branch started from: the committed change is included
        var git = new GitChanges();
        var files = (await git.StatusAsync(w.Path, w.BaseCommit)).Value!;
        Assert.Equal([new ChangedFile("notes.md", "?"), new ChangedFile("src/app.cs", "M")], files);
        var diff = DiffParser.Parse((await git.DiffAsync(w.Path, files[1], w.BaseCommit)).Value!);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "int a = 1;", null, 1), diff);
        // ...while against HEAD only the untracked file is left
        Assert.Equal([new ChangedFile("notes.md", "?")], (await git.StatusAsync(w.Path)).Value);

        // The main checkout is untouched
        Assert.Equal("int a;\n", File.ReadAllText(Path.Combine(Repo, "src", "app.cs")).Replace("\r\n", "\n"));

        var removed = await GitWorktrees.RemoveAsync(w);
        Assert.Null(removed.Error);
        Assert.False(Directory.Exists(w.Path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(w.Path))); // the empty <repo>.worktrees folder too
        Assert.False((await GitRunner.RunAsync(Repo, "rev-parse", "--verify", "--quiet", "refs/heads/kovan/test-1")).Ok);
    }

    [Fact]
    public async Task Create_ExplainsFailures()
    {
        CreateRepo();
        Assert.NotNull((await GitWorktrees.CreateAsync(Repo, "bad name")).Error);
        Assert.Null((await GitWorktrees.CreateAsync(Repo, "dup")).Error);
        var again = await GitWorktrees.CreateAsync(Repo, "dup");
        Assert.Contains("already exists", again.Error);
    }

    private void CreateRepo()
    {
        Directory.CreateDirectory(Path.Combine(Repo, "src"));
        Git(Repo, "init", "-q");
        Git(Repo, "config", "user.email", "test@example.com");
        Git(Repo, "config", "user.name", "Test");
        Git(Repo, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(Repo, "src", "app.cs"), "int a;\n");
        Git(Repo, "add", ".");
        Git(Repo, "commit", "-q", "-m", "init");
    }

    private static void Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["GIT_AUTHOR_NAME"] = psi.Environment["GIT_COMMITTER_NAME"] = "Test";
        psi.Environment["GIT_AUTHOR_EMAIL"] = psi.Environment["GIT_COMMITTER_EMAIL"] = "test@example.com";
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => true;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
    }
}
