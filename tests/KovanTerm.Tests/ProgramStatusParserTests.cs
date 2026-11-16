using System.Text;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class ProgramStatusParserTests
{
    private const string Esc = "\x1b";
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void FindsReportsInOutput_WithEitherTerminator()
    {
        // As captured from Codex 0.169 under ConPTY
        var output = $"text{Esc}[1mbold{Esc}]7501;state=working:app=codex{Esc}\\more{Esc}]7501;state=blocked:app=codex:kind=permission\x07";
        var reports = new ProgramStatusParser().Feed(output);
        Assert.Equal(
            [new ProgramStatusReport("working", App: "codex"), new ProgramStatusReport("blocked", "permission", App: "codex")],
            reports);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]   // inside the prefix
    [InlineData(20)]  // inside the body
    [InlineData(30)]  // between ESC and \
    public void ReportSplitAcrossChunks_IsCompleted(int split)
    {
        var output = $"ab{Esc}]7501;state=done:app=codex{Esc}\\cd";
        var parser = new ProgramStatusParser();
        var first = parser.Feed(output[..split]);
        var second = parser.Feed(output[split..]);
        Assert.Empty(first);
        Assert.Equal([new ProgramStatusReport("done", App: "codex")], second);
    }

    [Fact]
    public void Message_IsDecodedAndSanitized()
    {
        var r = ProgramStatusParser.Parse($"state=working:msg={B64("Running‮ tests\n")}");
        Assert.Equal("Running tests", r!.Message);
        Assert.Null(ProgramStatusParser.Parse("state=working:msg=@@@")!.Message);
    }

    [Theory]
    [InlineData("?")]                 // the probe
    [InlineData("state=sleeping")]
    [InlineData("app=codex")]
    public void NonReports_AreIgnored(string body) => Assert.Null(ProgramStatusParser.Parse(body));

    [Fact]
    public void PlainOutput_IsCheap_AndUnterminatedGarbageIsDropped()
    {
        var parser = new ProgramStatusParser();
        Assert.Empty(parser.Feed("hello world"));
        Assert.Empty(parser.Feed($"{Esc}]7501;" + new string('x', 5000)));
        Assert.Equal([new ProgramStatusReport("idle")], parser.Feed($"{Esc}]7501;state=idle\x07"));
    }

    [Fact]
    public void Reports_DriveTheNode_AndCodexAllowsWithY()
    {
        // Started through a wrapper the command line doesn't reveal: the key is learned from the reports
        var node = new TerminalNodeViewModel { Title = "Codex", CommandLine = "node run-agent.js" };
        Assert.Equal("1", node.AllowKey);

        node.ApplyProgramStatus(new("blocked", "permission", App: "codex"));
        Assert.Equal(NodeStatus.Waiting, node.Status);
        Assert.True(node.AwaitsPermission);
        Assert.Equal("y", node.AllowKey);

        node.ApplyProgramStatus(new("working", App: "codex", Message: "Editing"));
        Assert.Equal("Working · Editing", node.StatusText);

        // A subagent's record doesn't change the node
        node.ApplyProgramStatus(new("blocked", "permission", Id: "sub/1"));
        Assert.Equal(NodeStatus.Working, node.Status);

        node.ApplyProgramStatus(new("blocked", "question"));
        Assert.Equal(NodeStatus.Waiting, node.Status);
        Assert.False(node.AwaitsPermission);
    }
}
