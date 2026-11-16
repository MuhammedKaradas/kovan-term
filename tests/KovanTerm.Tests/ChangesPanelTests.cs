using System.Diagnostics;
using System.IO;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class ChangesPanelTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "kovan-git-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_repo)) return;
        // git marks object files read-only
        foreach (var f in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_repo, recursive: true);
    }

    [Fact]
    public void DiffParser_NumbersLines()
    {
        var diff = """
            diff --git a/app.cs b/app.cs
            index 111..222 100644
            --- a/app.cs
            +++ b/app.cs
            @@ -10,3 +10,4 @@ class App
             int a;
            -int b;
            +int b = 2;
            +int c;
             int d;
            """;
        var lines = DiffParser.Parse(diff).Where(l => l.Kind != DiffLineKind.Header).ToList();
        Assert.Equal(
            [
                new DiffLine(DiffLineKind.Hunk, "@@ -10,3 +10,4 @@ class App", null, null),
                new DiffLine(DiffLineKind.Context, "int a;", 10, 10),
                new DiffLine(DiffLineKind.Removed, "int b;", 11, null),
                new DiffLine(DiffLineKind.Added, "int b = 2;", null, 11),
                new DiffLine(DiffLineKind.Added, "int c;", null, 12),
                new DiffLine(DiffLineKind.Context, "int d;", 12, 13),
            ],
            lines);
        Assert.Equal(11, lines[2].Line); // a removed line is referred to by its old number
    }

    [Fact]
    public void DiffParser_KeepsNotesAndSkipsHeaders()
    {
        var lines = DiffParser.Parse("diff --git a/x.png b/x.png\nindex 1..2\nBinary files a/x.png and b/x.png differ\n");
        Assert.Equal(DiffLineKind.Note, lines.Single(l => l.Kind != DiffLineKind.Header).Kind);
        var noNewline = DiffParser.Parse("@@ -1 +1 @@\n-a\n+b\n\\ No newline at end of file\n");
        Assert.Equal(DiffLineKind.Note, noNewline[^1].Kind);
    }

    [Fact]
    public void ParseStatus_ReadsCodesAndRenames()
    {
        var files = GitChanges.ParseStatus(" M src/app.cs\0?? notes/new file.md\0R  b.txt\0a.txt\0 D gone.txt\0A  added.cs\0");
        Assert.Equal(
            [
                new ChangedFile("added.cs", "A"),
                new ChangedFile("b.txt", "R", "a.txt"),
                new ChangedFile("gone.txt", "D"),
                new ChangedFile("notes/new file.md", "?"),
                new ChangedFile("src/app.cs", "M"),
            ],
            files);
        Assert.Equal("notes", files[3].Folder);
        Assert.Equal("new file.md", files[3].FileName);
    }

    [Fact]
    public void Notes_BecomeOneMessage_PastedWithoutSubmitting()
    {
        var message = ChangesViewModel.ComposeMessage(
        [
            new ReviewNote("src/app.cs", 11, "int b = 2;", "Use a constant"),
            new ReviewNote("README.md", null, "", "Mention the new flag\nin the usage section"),
        ]);
        Assert.Equal(
            "Review notes on your changes:\n\n- src/app.cs:11 `int b = 2;`: Use a constant\n- README.md: Mention the new flag\n  in the usage section",
            message);

        var paste = ChangesViewModel.BracketedPaste("a\x1b[31mb\nc");
        Assert.Equal("\x1b[200~a[31mb\nc\x1b[201~", paste); // no stray escapes, no trailing Enter
    }

    [Fact]
    public async Task GitChanges_ListsChangesAndDiffs_OfARealRepository()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        Git("init", "-q");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "Test");
        File.WriteAllText(Path.Combine(_repo, "src", "app.cs"), "int a;\nint b;\n");
        Git("add", ".");
        Git("commit", "-q", "-m", "init");

        File.WriteAllText(Path.Combine(_repo, "src", "app.cs"), "int a;\nint b = 2;\n");
        File.WriteAllText(Path.Combine(_repo, "new.txt"), "hello\n");

        var git = new GitChanges();
        // From a subfolder too: paths are relative to the repository root
        var status = await git.StatusAsync(Path.Combine(_repo, "src"));
        Assert.Null(status.Error);
        Assert.Equal([new ChangedFile("new.txt", "?"), new ChangedFile("src/app.cs", "M")], status.Value);

        var modified = DiffParser.Parse((await git.DiffAsync(_repo, status.Value![1])).Value!);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "int b = 2;", null, 2), modified);
        Assert.Contains(new DiffLine(DiffLineKind.Removed, "int b;", 2, null), modified);

        var untracked = DiffParser.Parse((await git.DiffAsync(_repo, status.Value[0])).Value!);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "hello", null, 1), untracked);
    }

    [Fact]
    public async Task GitChanges_OutsideARepository_SaysSo()
    {
        Directory.CreateDirectory(_repo);
        var status = await new GitChanges().StatusAsync(_repo);
        Assert.Null(status.Value);
        Assert.Equal("Not a git repository.", status.Error);
    }

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}
