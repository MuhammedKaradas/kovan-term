using KovanTerm.Models;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>What the Windows notification says while Kovan is in the background (#80).</summary>
public sealed class AgentNotificationsTests
{
    private static TerminalNodeViewModel Agent(string title, string command = "claude.exe") =>
        TerminalNodeViewModel.FromState(new NodeState { Title = title, CommandLine = command, Width = 400, Height = 300 });

    [Fact]
    public void OneWaiting_SaysWhoAndWhy()
    {
        var claude = Agent("Claude");
        claude.StatusText = "Allow Bash: npm test?";
        var n = new AgentNotifications();

        var notice = n.Waiting(claude)!;

        Assert.Equal("Claude needs you", notice.Title);
        Assert.Equal("Allow Bash: npm test?", notice.Text);
        Assert.Same(claude, notice.Target);
        Assert.True(notice.NeedsYou);
    }

    [Fact]
    public void SeveralWaiting_AreSummedUp_AndTheClickGoesToTheFirst()
    {
        var (claude, codex) = (Agent("Claude"), Agent("Codex", "codex.exe"));
        var n = new AgentNotifications();
        n.Waiting(claude);

        var notice = n.Waiting(codex)!;

        Assert.Equal("2 agents need you", notice.Title);
        Assert.Equal("Claude, Codex", notice.Text);
        Assert.Same(claude, notice.Target);
    }

    [Fact]
    public void Finished_AloneOrNextToWaiting()
    {
        var (claude, codex, gemini) = (Agent("Claude"), Agent("Codex", "codex.exe"), Agent("Gemini", "gemini.exe"));
        var n = new AgentNotifications();

        var one = n.Finished(codex)!;
        Assert.Equal(("Codex finished", "Its turn is over.", false), (one.Title, one.Text, one.NeedsYou));

        Assert.Equal("2 agents finished", n.Finished(gemini)!.Title);

        var both = n.Waiting(claude)!;
        Assert.Equal("Claude needs you", both.Title);
        Assert.EndsWith("\nAlso finished: Codex, Gemini", both.Text);
    }

    [Fact]
    public void AnAgentThatFinishes_IsNoLongerWaiting_AndAShellIsntAnAgent()
    {
        var claude = Agent("Claude");
        var n = new AgentNotifications();
        n.Waiting(claude);

        Assert.Equal("Claude finished", n.Finished(claude)!.Title);
        Assert.Null(n.Finished(Agent("Shell", "cmd.exe")));
    }

    [Fact]
    public void Settings_TurnEachKindOff()
    {
        var n = new AgentNotifications { NotifyWhenDone = false };
        Assert.Null(n.Finished(Agent("Codex", "codex.exe")));
        Assert.NotNull(n.Waiting(Agent("Claude")));

        var quiet = new AgentNotifications { NotifyWhenWaiting = false };
        Assert.Null(quiet.Waiting(Agent("Claude")));
    }

    [Fact]
    public void Clear_And_Prune()
    {
        var (claude, codex) = (Agent("Claude"), Agent("Codex", "codex.exe"));
        var n = new AgentNotifications();
        claude.Status = NodeStatus.Waiting;
        n.Waiting(claude);
        n.Waiting(codex);

        // Codex was answered in its terminal meanwhile (no longer waiting); Claude still is
        n.Prune([claude, codex]);
        Assert.Equal("Claude needs you", n.Describe()!.Title);

        // Claude was closed
        n.Prune([codex]);
        Assert.Null(n.Describe());

        n.Waiting(claude);
        n.Clear();
        Assert.Null(n.Describe());
    }

    [Fact]
    public void OnAnotherWorkspace_SaysWhere()
    {
        var claude = Agent("Claude");
        claude.OtherWorkspaceName = "Kovan";

        Assert.Equal("It's waiting for your answer.\n(in Kovan)", new AgentNotifications().Waiting(claude)!.Text);
    }
}
