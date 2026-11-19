using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Claude Code nodes named after their conversation (/rename, Claude's own title).</summary>
public sealed class ConversationTitleTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N") + ".jsonl");

    public void Dispose() => File.Delete(_file);

    private static TerminalNodeViewModel Node(NodeState? s = null) =>
        TerminalNodeViewModel.FromState(s ?? new NodeState { Title = "Claude Code", CommandLine = "claude.exe", Width = 400, Height = 300 });

    private void Append(params string[] lines) => File.AppendAllLines(_file, lines);

    [Fact]
    public void Transcript_KeepsTheLatestTitles_NextToTheContext()
    {
        Append("""{"type":"user","message":{"content":"hi"}}""",
            """{"type":"ai-title","aiTitle":"Fix the login page","sessionId":"x"}""");
        var usage = new TranscriptUsage();

        Assert.True(usage.Update(_file));
        Assert.Equal((null, "Fix the login page"), (usage.CustomTitle, usage.AiTitle));
        Assert.False(usage.Update(_file));

        Append("""{"type":"custom-title","customTitle":"Login\nbug","sessionId":"x"}""",
            """{"type":"ai-title","aiTitle":"Login form validation","sessionId":"x"}""");
        Assert.True(usage.Update(_file));
        Assert.Equal(("Login bug", "Login form validation"), (usage.CustomTitle, usage.AiTitle));
    }

    [Fact]
    public void ClaudesTitle_NamesANodeTheUserDidntName()
    {
        var node = Node();

        Assert.True(node.ApplyConversationTitle(null, "Fix the login page"));
        Assert.Equal("Fix the login page", node.Title);
        Assert.True(node.ApplyConversationTitle(null, "Login form validation"));
        Assert.Equal("Login form validation", node.Title);
    }

    [Fact]
    public void ANameTheUserTyped_StaysUntilTheConversationIsRenamed()
    {
        var node = Node();
        node.ApplyConversationTitle(null, "Fix the login page");

        node.Rename("  Login  ");
        Assert.Equal("Login", node.Title);

        Assert.False(node.ApplyConversationTitle(null, "Login form validation"));
        Assert.Equal("Login", node.Title);

        Assert.True(node.ApplyConversationTitle("Auth rewrite", "Login form validation"));
        Assert.Equal("Auth rewrite", node.Title);
        Assert.False(node.TitleSetByUser);
    }

    [Fact]
    public void ARename_WinsOverClaudesLaterTitles()
    {
        var node = Node();
        node.ApplyConversationTitle("Auth rewrite", "Fix the login page");

        Assert.False(node.ApplyConversationTitle("Auth rewrite", "Something else"));
        Assert.Equal("Auth rewrite", node.Title);
    }

    [Fact]
    public void TitlesAlreadySeen_DontComeBackAfterARestart()
    {
        var node = Node();
        node.ApplyConversationTitle("Auth rewrite", null);
        node.Rename("Mine");

        var restored = Node(node.ToState());

        Assert.Equal(("Mine", true), (restored.Title, restored.TitleSetByUser));
        // The first read after the restart sees the same /rename: nothing changes
        Assert.False(restored.ApplyConversationTitle("Auth rewrite", "Fix the login page"));
        Assert.Equal("Mine", restored.Title);
        Assert.True(restored.ApplyConversationTitle("New name", null));
    }

    [Fact]
    public void AnEmptyName_IsIgnored()
    {
        var node = Node();
        node.Rename("   ");
        Assert.Equal(("Claude Code", false), (node.Title, node.TitleSetByUser));
    }
}
