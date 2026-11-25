using System.Diagnostics;
using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Changes panel "viewed" boxes, and a pull request that conflicts with its base branch (#142).</summary>
public sealed class ViewedAndConflictsTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "kovan-git-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_repo)) return;
        foreach (var f in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_repo, recursive: true);
    }

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    private async Task<ChangesViewModel> RepoWithTwoChanges()
    {
        Directory.CreateDirectory(_repo);
        Git("init", "-q");
        Git("config", "user.email", "t@example.com");
        Git("config", "user.name", "t");
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "a\n");
        File.WriteAllText(Path.Combine(_repo, "b.txt"), "b\n");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "a2\n");
        File.WriteAllText(Path.Combine(_repo, "b.txt"), "b2\n");
        var vm = new ChangesViewModel(new GitChanges());
        await vm.ShowAsync(TerminalNodeViewModel.FromState(new NodeState
        {
            Id = Guid.NewGuid(), Title = "c", CommandLine = "cmd", WorkingDirectory = _repo,
        }));
        return vm;
    }

    [Fact]
    public async Task A_viewed_file_stays_viewed_until_it_changes()
    {
        var vm = await RepoWithTwoChanges();
        Assert.Equal("2 to review", vm.UnviewedText);

        vm.Files.Single(f => f.Path == "a.txt").IsViewed = true;
        Assert.Equal("1 to review", vm.UnviewedText);

        await vm.RefreshAsync();
        Assert.True(vm.Files.Single(f => f.Path == "a.txt").IsViewed);
        Assert.False(vm.Files.Single(f => f.Path == "b.txt").IsViewed);

        // The agent edits it again
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "a3, longer\n");
        await vm.RefreshAsync();
        Assert.False(vm.Files.Single(f => f.Path == "a.txt").IsViewed);
        Assert.Equal("2 to review", vm.UnviewedText);

        foreach (var f in vm.Files) f.IsViewed = true;
        Assert.Equal("All viewed", vm.UnviewedText);
    }

    [Fact]
    public void Fingerprint_follows_the_file()
    {
        Directory.CreateDirectory(_repo);
        var file = new ChangedFile("x.txt", "M");
        File.WriteAllText(Path.Combine(_repo, "x.txt"), "1");
        var first = ViewedFiles.Fingerprint(_repo, file);
        Assert.Equal(first, ViewedFiles.Fingerprint(_repo, file));
        File.WriteAllText(Path.Combine(_repo, "x.txt"), "12");
        Assert.NotEqual(first, ViewedFiles.Fingerprint(_repo, file));
        File.Delete(Path.Combine(_repo, "x.txt"));
        Assert.Equal("M:gone", ViewedFiles.Fingerprint(_repo, file));
    }

    private static string Json(string state, string mergeable) =>
        $$"""{"baseRefName":"main","headRefOid":"abc","isDraft":false,"mergeable":"{{mergeable}}","number":7,"state":"{{state}}","statusCheckRollup":[],"url":"u"}""";

    [Theory]
    [InlineData("OPEN", "CONFLICTING", true)]
    [InlineData("OPEN", "MERGEABLE", false)]
    // GitHub hasn't worked it out yet: the next refresh will know
    [InlineData("OPEN", "UNKNOWN", false)]
    [InlineData("MERGED", "CONFLICTING", false)]
    public void Conflicts_are_read_from_gh(string state, string mergeable, bool conflicts)
    {
        var pr = PullRequests.Parse(Json(state, mergeable))!;
        Assert.Equal(conflicts, pr.HasConflicts);
        Assert.Equal("main", pr.BaseBranch);
    }

    [Fact]
    public void The_prompt_names_the_base_branch()
    {
        var prompt = PullRequests.ConflictPrompt(PullRequests.Parse(Json("OPEN", "CONFLICTING"))!);
        Assert.Contains("#7", prompt);
        Assert.Contains("origin/main", prompt);
        Assert.Contains("resolve the conflicts", prompt);
    }
}
