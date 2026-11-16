using System.IO;
using System.Text;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class RecentConversationsTests : IDisposable
{
    private readonly string _projects = Path.Combine(Path.GetTempPath(), "kovan-convs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_projects)) Directory.Delete(_projects, recursive: true);
    }

    private static string User(string text, string cwd = @"C:\work\shop", bool meta = false) =>
        "{\"type\":\"user\",\"cwd\":" + Json(cwd) + ",\"isMeta\":" + (meta ? "true" : "false") +
        ",\"message\":{\"role\":\"user\",\"content\":" + Json(text) + "}}";

    private static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    private string Write(string folder, Guid id, params string[] lines)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_projects, folder)).FullName;
        var path = Path.Combine(dir, id + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    [Fact]
    public void Title_PrefersRenameThenGeneratedThenFirstPrompt()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        Write("C--work-shop", a, User("Fix the login bug"), """{"type":"ai-title","aiTitle":"Login bug fix"}""",
            """{"type":"custom-title","customTitle":"Auth work"}""");
        Write("C--work-shop", b, User("Add a dark theme"), """{"type":"ai-title","aiTitle":"Old"}""", """{"type":"ai-title","aiTitle":"Dark theme"}""");
        Write("C--work-shop", c, User("<system-reminder>x</system-reminder>", meta: true), User("<pasted_content id=\"1\">\n</pasted_content>\nShip the release notes"));

        var list = new ClaudeConversations(_projects).List().ToDictionary(x => x.SessionId);
        Assert.Equal("Auth work", list[a.ToString()].Title);
        Assert.Equal("Dark theme", list[b.ToString()].Title);
        Assert.Equal("Ship the release notes", list[c.ToString()].Title);
        Assert.Equal(@"C:\work\shop", list[a.ToString()].Folder);
    }

    [Fact]
    public void SkipsEmptyConversations_SubagentFiles_AndNonIdFiles()
    {
        Write("p", Guid.NewGuid(), """{"type":"permission-mode","permissionMode":"default"}""");
        Write("p", Guid.NewGuid(), User("<command-name>/clear</command-name>"));
        var sub = Directory.CreateDirectory(Path.Combine(_projects, "p", Guid.NewGuid().ToString(), "subagents")).FullName;
        File.WriteAllText(Path.Combine(sub, "agent-1.jsonl"), User("subagent prompt") + "\n");
        File.WriteAllText(Path.Combine(_projects, "p", "notes.jsonl"), User("not a conversation") + "\n");
        Assert.Empty(new ClaudeConversations(_projects).List());
    }

    [Fact]
    public void LargeTranscript_ReadsHeadAndTailOnly()
    {
        var id = Guid.NewGuid();
        var filler = new StringBuilder();
        var big = new string('x', 2000);
        for (var i = 0; i < 2000; i++) filler.Append("{\"type\":\"assistant\",\"message\":{\"content\":\"" + big + "\"}}").Append('\n');
        var path = Write("big", id, User("Start here"), filler.ToString().TrimEnd('\n'), """{"type":"ai-title","aiTitle":"Final title"}""");
        Assert.True(new FileInfo(path).Length > 3_000_000);
        var info = ClaudeConversations.Read(new FileInfo(path))!;
        Assert.Equal("Final title", info.Title);
        Assert.Equal(@"C:\work\shop", info.Folder);
    }

    [Fact]
    public void NewestFirst_AndLongTitlesAreShortened()
    {
        var old = Write("p", Guid.NewGuid(), User("old one"));
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-3));
        Write("p", Guid.NewGuid(), User(new string('y', 300)));
        var list = new ClaudeConversations(_projects).List();
        Assert.Equal(2, list.Count);
        Assert.EndsWith("…", list[0].Title);
        Assert.Equal("old one", list[1].Title);
    }

    [Fact]
    public void Dialog_SearchesAndMarksOpenOnes()
    {
        var now = new DateTime(2026, 11, 17, 12, 0, 0);
        var convs = new[]
        {
            new ConversationInfo("1", @"C:\work\shop", "Auth work", now.AddMinutes(-5), "a"),
            new ConversationInfo("2", @"C:\work\blog", "Dark theme", now.AddHours(-3), "b"),
            new ConversationInfo("3", null, "Lost folder", now.AddDays(-10), "c"),
        };
        var vm = new RecentConversationsViewModel(convs, new HashSet<string> { "1" }, now);
        Assert.True(vm.Rows[0].IsOpen);
        Assert.Equal("2", vm.Selected!.Info.SessionId); // first one not already open
        Assert.Equal("5 min ago", vm.Rows[0].When);
        Assert.Equal("3 h ago", vm.Rows[1].When);
        Assert.Equal("7 Nov 2026", vm.Rows[2].When);

        vm.Search = "blog";
        Assert.Equal(["2"], vm.Rows.Select(r => r.Info.SessionId));
        vm.Search = "dark THEME";
        Assert.Single(vm.Rows);

        vm.Search = "lost";
        Assert.False(vm.OpenCommand.CanExecute(null)); // no folder to open it in
    }
}
