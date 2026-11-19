using KovanTerm.Services;

namespace KovanTerm.Tests;

/// <summary>A worktree branch's pull request, read from gh (#86).</summary>
public sealed class PullRequestsTests
{
    private static string Json(string state, string checks, bool draft = false) =>
        $$"""{"headRefOid":"abc123","isDraft":{{(draft ? "true" : "false")}},"number":85,"state":"{{state}}","statusCheckRollup":[{{checks}}],"url":"https://github.com/o/r/pull/85"}""";

    private const string Running = """{"__typename":"CheckRun","conclusion":"","detailsUrl":"https://github.com/o/r/actions/runs/446/job/1","name":"build","status":"IN_PROGRESS"}""";
    private const string Passed = """{"__typename":"CheckRun","conclusion":"SUCCESS","detailsUrl":"https://github.com/o/r/actions/runs/447/job/1","name":"build","status":"COMPLETED"}""";
    private const string Failed = """{"__typename":"CheckRun","conclusion":"FAILURE","detailsUrl":"https://github.com/o/r/actions/runs/448/job/2","name":"test","status":"COMPLETED"}""";
    private const string StatusError = """{"__typename":"StatusContext","context":"ci/legacy","state":"ERROR","targetUrl":"https://ci.example.com/9"}""";

    [Fact]
    public void Open_WithItsChecks()
    {
        var running = PullRequests.Parse(Json("OPEN", Running))!;
        Assert.Equal((85, PullRequestState.Open, ChecksState.Pending, "abc123"), (running.Number, running.State, running.Checks, running.HeadCommit));
        Assert.Equal("PR #85 · checks running", running.Summary);

        Assert.Equal("PR #85 · checks passed", PullRequests.Parse(Json("OPEN", Passed))!.Summary);
        Assert.Equal("PR #85 · open", PullRequests.Parse(Json("OPEN", ""))!.Summary);
        Assert.Equal("PR #85 · draft, checks passed", PullRequests.Parse(Json("OPEN", Passed, draft: true))!.Summary);
    }

    [Fact]
    public void Failing_ListsWhatFailed_WithTheirActionsRun()
    {
        var pr = PullRequests.Parse(Json("OPEN", $"{Passed},{Failed},{StatusError},{Running}"))!;

        Assert.Equal(ChecksState.Failing, pr.Checks);
        Assert.Equal([new FailedCheck("test", "https://github.com/o/r/actions/runs/448/job/2", 448), new FailedCheck("ci/legacy", "https://ci.example.com/9", null)],
            pr.Failed);
        Assert.Equal("PR #85 · checks failing", pr.Summary);
    }

    [Fact]
    public void MergedAndClosed()
    {
        Assert.Equal("PR #85 · merged", PullRequests.Parse(Json("MERGED", Passed))!.Summary);
        Assert.Equal(PullRequestState.Closed, PullRequests.Parse(Json("CLOSED", ""))!.State);
    }

    [Fact]
    public void NotJson_IsNoPullRequest() => Assert.Null(PullRequests.Parse("no pull requests found for branch \"x\""));

    [Fact]
    public void Log_IsCleanedOfColorsAndControlCharacters()
    {
        Assert.Equal("test\tRun npm test\t2026 Error: expected 2\nnext",
            PullRequests.Clean("﻿test\tRun npm test\t2026 \x1b[31mError:\x1b[0m expected 2\r\nnext\x07"));
    }

    [Fact]
    public void FixPrompt_SaysWhatFailedAndShowsTheLog()
    {
        var pr = PullRequests.Parse(Json("OPEN", Failed))!;

        var prompt = PullRequests.FixPrompt(pr, "test\tRun npm test\tError: expected 2\n");

        Assert.StartsWith("The checks of pull request #85 failed:\n- test (https://github.com/o/r/actions/runs/448/job/2)\n", prompt);
        Assert.Contains("```\ntest\tRun npm test\tError: expected 2\n```", prompt);
        Assert.EndsWith("Find out why and fix it.", prompt);
        Assert.DoesNotContain("```", PullRequests.FixPrompt(pr, "  "));
    }
}
