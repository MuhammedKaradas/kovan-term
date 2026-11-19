using KovanTerm.Services.Voice;
using static KovanTerm.Services.Voice.VoiceCommandKind;

namespace KovanTerm.Tests;

/// <summary>Voice commands in Turkish and English (#21).</summary>
public sealed class VoiceCommandsTests
{
    private static readonly VoiceTarget Claude = new("claude", "Claude (a1b2)");
    private static readonly VoiceTarget Codex = new("codex", "Codex");
    private static readonly VoiceTarget Gemini = new("gemini", "Gemini CLI");
    private static readonly VoiceTarget Shell = new("shell", "PowerShell");
    private static readonly VoiceTarget[] Nodes = [Claude, Codex, Gemini, Shell];
    private static readonly VoiceTarget[] Workspaces = [new("w1", "Workspace 1"), new("kovan", "Kovan"), new("site", "Web site")];

    private static VoiceCommand Parse(string heard) => VoiceCommands.Parse(heard, Nodes, Workspaces);

    [Theory]
    [InlineData("Bekleyen ajana git.")]
    [InlineData("beni bekleyen terminali göster")]
    [InlineData("Go to the waiting agent.")]
    [InlineData("Show me what's waiting")]
    [InlineData("Bekle yanıcana git.")] // "bekleyen ajana", misheard
    public void GoToWaiting(string heard) => Assert.Equal(VoiceCommandKind.GoToWaiting, Parse(heard).Kind);

    [Theory]
    [InlineData("Claude'a git.", "claude")]
    [InlineData("Codex'e geç", "codex")]
    [InlineData("Codexe geç", "codex")]
    [InlineData("Gemini'ye git", "gemini")]
    [InlineData("Go to Claude.", "claude")]
    [InlineData("switch to codex", "codex")]
    [InlineData("Show me the PowerShell", "shell")]
    [InlineData("Claud'a git", "claude")] // a letter off
    // As Whisper writes them when a Turkish speaker says them
    [InlineData("Klod'a git", "claude")]
    [InlineData("Kodeks'e geç", "codex")]
    [InlineData("Cemini'ye git", "gemini")]
    public void GoTo(string heard, string node)
    {
        var c = Parse(heard);
        Assert.Equal(VoiceCommandKind.GoTo, c.Kind);
        Assert.Equal([node], c.Targets);
    }

    [Fact]
    public void GoTo_SomethingThatIsntThere_SaysSo()
    {
        var c = Parse("Copilot'a git");
        Assert.Equal(VoiceCommandKind.GoTo, c.Kind);
        Assert.True(c.NameNotFound);
        Assert.Equal("copilot'a", c.NameHeard);
    }

    [Fact]
    public void TwoWithTheSameName_AreBothReturned()
    {
        var two = VoiceCommands.Parse("Claude'a git", [Claude, new VoiceTarget("claude2", "Claude (c3d4)")], []);
        Assert.Equal(["claude", "claude2"], two.Targets);
    }

    [Theory]
    [InlineData("Claude'a söyle testleri çalıştırsın.", "claude", "testleri çalıştır")]
    [InlineData("Claude'a söyle: testleri çalıştır ve hataları düzelt.", "claude", "testleri çalıştır ve hataları düzelt")]
    [InlineData("Codex'e testleri çalıştırmasını söyle", "codex", "testleri çalıştır")]
    [InlineData("Giriş sayfasını düzeltmesini Claude'a söyle.", "claude", "Giriş sayfasını düzelt")]
    [InlineData("Tell Claude to run the tests.", "claude", "run the tests")]
    [InlineData("Ask Gemini to review the last commit", "gemini", "review the last commit")]
    [InlineData("tell codex that the build is broken", "codex", "the build is broken")]
    [InlineData("Tel codex to check the build.", "codex", "check the build")]
    [InlineData("Klod'a söyle testleri çalıştırsın.", "claude", "testleri çalıştır")]
    public void Tell(string heard, string node, string text)
    {
        var c = Parse(heard);
        Assert.Equal(VoiceCommandKind.Tell, c.Kind);
        Assert.Equal([node], c.Targets);
        Assert.Equal(text, c.Text);
    }

    [Theory]
    [InlineData("İzin ver", Allow, null)]
    [InlineData("Onayla.", Allow, null)]
    [InlineData("Claude'a izin ver", Allow, "claude")]
    [InlineData("bekleyen ajana izin ver", Allow, null)]
    [InlineData("Allow it.", Allow, null)]
    [InlineData("approve codex", Allow, "codex")]
    [InlineData("İzin verme", Deny, null)]
    [InlineData("Reddet", Deny, null)]
    [InlineData("Deny.", Deny, null)]
    [InlineData("deny the request for Claude", Deny, "claude")]
    public void Answers(string heard, VoiceCommandKind kind, string? node)
    {
        var c = Parse(heard);
        Assert.Equal(kind, c.Kind);
        Assert.Equal(node == null ? [] : new object[] { node }, c.Targets);
    }

    [Theory]
    [InlineData("Codex'i kapat", "codex")]
    [InlineData("Close PowerShell.", "shell")]
    public void Close(string heard, string node)
    {
        var c = Parse(heard);
        Assert.Equal(VoiceCommandKind.Close, c.Kind);
        Assert.Equal([node], c.Targets);
    }

    [Theory]
    [InlineData("Hepsini göster", FitAll)]
    [InlineData("Tümünü sığdır.", FitAll)]
    [InlineData("Fit all", FitAll)]
    [InlineData("show everything", FitAll)]
    [InlineData("Yakınlaş", Zoom)]
    [InlineData("zoom in", Zoom)]
    [InlineData("Yeni terminal aç", NewTerminal)]
    [InlineData("Open a new terminal.", NewTerminal)]
    [InlineData("Sonraki çalışma alanı", NextWorkspace)]
    [InlineData("next workspace", NextWorkspace)]
    [InlineData("Önceki alana geç", PreviousWorkspace)]
    [InlineData("Panoyu aç", ToggleBoard)]
    [InlineData("hide the board", ToggleBoard)]
    [InlineData("Değişiklikleri göster", ToggleChanges)]
    [InlineData("Show changes", ToggleChanges)]
    [InlineData("Ajanlar panelini kapat", ToggleAgents)]
    [InlineData("toggle the agents panel", ToggleAgents)]
    [InlineData("Bugün hava çok güzel", Unknown)]
    [InlineData("", Unknown)]
    public void Others(string heard, VoiceCommandKind kind) => Assert.Equal(kind, Parse(heard).Kind);

    [Fact]
    public void Zoom_ToANamedNode()
    {
        Assert.Equal(["codex"], Parse("Codex'e yakınlaş").Targets);
        Assert.Equal(["claude"], Parse("zoom to Claude").Targets);
    }

    [Theory]
    [InlineData("Kovan alanına geç", "kovan")]
    [InlineData("Web site çalışma alanına git", "site")]
    [InlineData("Switch to the Kovan workspace", "kovan")]
    [InlineData("go to workspace 1", "w1")]
    public void SwitchWorkspace(string heard, string workspace)
    {
        var c = Parse(heard);
        Assert.Equal(VoiceCommandKind.SwitchWorkspace, c.Kind);
        Assert.Equal([workspace], c.Targets);
    }

    [Theory]
    [InlineData("Not al: yarın sürümü çıkar.", "yarın sürümü çıkar")]
    [InlineData("Take a note that the login page is slow", "the login page is slow")]
    [InlineData("yeni not", "")]
    public void NewNote(string heard, string text)
    {
        var c = Parse(heard);
        Assert.Equal(VoiceCommandKind.NewNote, c.Kind);
        Assert.Equal(text, c.Text);
    }

    [Fact]
    public void Fold_KeepsPositions()
    {
        const string s = "Claude'a SÖYLE: Işık İyi.";
        Assert.Equal(s.Length, VoiceCommands.Fold(s).Length);
        Assert.Equal("claude'a soyle  isik iyi ", VoiceCommands.Fold(s));
    }
}
