using System.IO;
using KovanTerm.Services.Stats;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Statistics read from Claude Code's and Codex's session files.</summary>
public sealed class WorkStatsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public WorkStatsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string Utc(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static readonly DateTime T0 = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Local);

    private static string User(DateTime at, string text, bool sidechain = false) =>
        $$$$"""{"type":"user","timestamp":"{{{{Utc(at)}}}}","cwd":"C:\\code\\shop","isSidechain":{{{{(sidechain ? "true" : "false")}}}},"message":{"role":"user","content":"{{{{text}}}}"}}""";

    private static string Reply(DateTime at, string id, int input, int output, int cacheRead = 0) =>
        $$$$"""{"type":"assistant","timestamp":"{{{{Utc(at)}}}}","cwd":"C:\\code\\shop","message":{"id":"{{{{id}}}}","model":"claude-opus-5-5","usage":{"input_tokens":{{{{input}}}},"output_tokens":{{{{output}}}},"cache_creation_input_tokens":0,"cache_read_input_tokens":{{{{cacheRead}}}}}}}""";

    [Fact]
    public void Claude_TimeTokensPromptsAndTitle()
    {
        var lines = new[]
        {
            User(T0, "fix the cart"),
            Reply(T0.AddMinutes(1), "msg_1", 10, 5, 100),
            // A reply streamed in two parts repeats its usage: counted once
            Reply(T0.AddMinutes(1), "msg_1", 10, 5, 100),
            Reply(T0.AddMinutes(8), "msg_2", 20, 7),
            // 30 minutes later: a new stretch
            User(T0.AddMinutes(38), "<command-name>/clear</command-name>"),
            User(T0.AddMinutes(40), "now the checkout"),
            Reply(T0.AddMinutes(45), "msg_3", 1, 1),
            """{"type":"ai-title","aiTitle":"Shop cart fixes"}""",
            "not json",
        };
        var subagent = new[] { Reply(T0.AddMinutes(9), "msg_sub", 1000, 0) };

        var s = WorkStats.ParseClaude("abc", lines, [subagent])!;

        Assert.Equal(("abc", "claude", "shop", "Shop cart fixes"), (s.Id, s.Agent, s.Project, s.Title));
        Assert.Equal(["fix the cart", "now the checkout"], s.Prompts.Select(p => p.Text));
        Assert.Equal([TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(7)], s.Spans.Select(sp => sp.Duration));
        var tokens = s.TokenDays.Single();
        Assert.Equal(("claude-opus-5-5", new WorkTokens(1031, 13, 0, 100)), (tokens.Model, tokens.Tokens));
    }

    [Fact]
    public void Claude_ABigLine_GivesItsTimeAndUsageWithoutBeingParsed()
    {
        var huge = new string('x', 70_000);
        var line = $$$$"""{"type":"assistant","message":{"id":"msg_big","model":"claude-opus-5-5","content":"{{{{huge}}}}","usage":{"input_tokens":3,"output_tokens":4,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}},"timestamp":"{{{{Utc(T0.AddMinutes(5))}}}}"}""";

        var s = WorkStats.ParseClaude("big", [User(T0, "go"), line], [])!;

        Assert.Equal(TimeSpan.FromMinutes(5), s.Spans.Single().Duration);
        Assert.Equal(7, s.TokenDays.Single().Tokens.Total);
    }

    [Fact]
    public void Claude_WithoutAConversation_IsNothing() =>
        Assert.Null(WorkStats.ParseClaude("x", ["""{"type":"ai-title","aiTitle":"t"}"""], []));

    [Fact]
    public void Codex_FolderModelTokensAndPrompts()
    {
        var lines = new[]
        {
            $$$$"""{"timestamp":"{{{{Utc(T0)}}}}","type":"session_meta","payload":{"id":"s1","cwd":"C:\\code\\api"}}""",
            $$$$"""{"timestamp":"{{{{Utc(T0)}}}}","type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"gpt-6-luna"}}}""",
            $$$$"""{"timestamp":"{{{{Utc(T0.AddMinutes(1))}}}}","type":"event_msg","payload":{"type":"item_completed","item":{"type":"UserMessage","content":[{"type":"text","text":"add a health endpoint"}]}}}""",
            $$$$"""{"timestamp":"{{{{Utc(T0.AddMinutes(3))}}}}","type":"token_usage_record","payload":{"usage":{"input_tokens":1000,"cached_input_tokens":400,"output_tokens":50}}}""",
            $$$$"""{"timestamp":"{{{{Utc(T0.AddMinutes(4))}}}}","type":"response_item","payload":{"type":"message","role":"assistant"}}""",
        };

        var s = WorkStats.ParseCodex("rollout", lines)!;

        Assert.Equal(("s1", "codex", "api", "add a health endpoint"), (s.Id, s.Agent, s.Project, s.Title));
        Assert.Equal(TimeSpan.FromMinutes(4), s.Spans.Single().Duration);
        Assert.Equal(("gpt-6-luna", new WorkTokens(600, 50, 0, 400)), (s.TokenDays.Single().Model, s.TokenDays.Single().Tokens));
    }

    [Fact]
    public void SessionsAtTheSameTime_CountOnce_AndDaysSplitAtMidnight()
    {
        var a = new WorkSpan(T0, T0.AddHours(1));
        var b = new WorkSpan(T0.AddMinutes(30), T0.AddHours(2));
        Assert.Equal(TimeSpan.FromHours(2), WorkStats.Union([a, b]).Single().Duration);

        var late = new WorkSpan(T0.Date.AddHours(23), T0.Date.AddDays(1).AddHours(1));
        var days = WorkStats.PerDay([late]);
        Assert.Equal([TimeSpan.FromHours(1), TimeSpan.FromHours(1)], days.Values);
    }

    [Fact]
    public void Load_ReadsOnlyWhatChanged()
    {
        var projects = Directory.CreateDirectory(Path.Combine(_dir, "projects", "C--code-shop")).FullName;
        var file = Path.Combine(projects, "abc.jsonl");
        File.WriteAllLines(file, [User(T0, "fix the cart"), Reply(T0.AddMinutes(2), "msg_1", 1, 1)]);
        var cache = Path.Combine(_dir, "cache.json");
        var files = WorkStats.Files(Path.Combine(_dir, "projects"), Path.Combine(_dir, "no-codex")).ToList();

        Assert.Single(WorkStats.Load(cache, files));
        Assert.True(File.Exists(cache));

        // From the cache: a file read again would see this change
        File.WriteAllText(cache, File.ReadAllText(cache).Replace("fix the cart", "from the cache"));
        Assert.Equal("from the cache", WorkStats.Load(cache, files).Single().Prompts[0].Text);

        File.AppendAllLines(file, [User(T0.AddMinutes(3), "and the tests")]);
        Assert.Equal(2, WorkStats.Load(cache, files).Single().Prompts.Count);

        File.Delete(file);
        Assert.Empty(WorkStats.Load(cache, WorkStats.Files(Path.Combine(_dir, "projects"), Path.Combine(_dir, "no-codex")).ToList()));
    }

    [Fact]
    public async Task Statistics_FollowThePeriod()
    {
        var now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Local);
        WorkSession Session(string project, DateTime start, int minutes, long tokens) =>
            new(project + start.Ticks, "claude", @"C:\code\" + project, project, [], [new WorkSpan(start, start.AddMinutes(minutes))],
                [new WorkTokenDay(DateOnly.FromDateTime(start), "claude-opus-5-5", new WorkTokens(Output: tokens))]);
        var sessions = new List<WorkSession>
        {
            Session("shop", now.AddHours(-2), 60, 1000),
            Session("api", now.AddHours(-2).AddMinutes(30), 60, 500),
            Session("shop", now.AddDays(-3), 30, 2000),
            Session("old", now.AddDays(-100), 120, 9000),
        };
        var vm = new StatisticsViewModel(_ => sessions, () => now);
        await vm.LoadAsync();

        // Last 7 days: today's two overlap (1.5 h of time), plus 30 min three days ago
        Assert.Equal(("2 h 00 min", "2", "2", "3", "3.5k"), (vm.ActiveTime, vm.ActiveDays, vm.ProjectCount, vm.SessionCount, vm.TokenCount));
        Assert.Equal(["shop", "api"], vm.Projects.Select(p => p.Name));
        Assert.Equal(1.0, vm.Projects[0].TimeFraction);

        vm.ShowDayCommand.Execute(DateOnly.FromDateTime(now.AddDays(-3)));
        Assert.Equal(("30 min", "1", "2k"), (vm.ActiveTime, vm.SessionCount, vm.TokenCount));
        Assert.Contains(vm.Weeks.SelectMany(w => w.Days), d => d.IsSelected && d.Date == DateOnly.FromDateTime(now.AddDays(-3)));

        vm.ShowPeriodCommand.Execute(StatsPeriod.Year);
        Assert.Equal("4", vm.SessionCount);
        // The calendar: 53 weeks, today the latest day not in the future, a busy day darker than a quiet one
        Assert.Equal(53, vm.Weeks.Count);
        var days = vm.Weeks.SelectMany(w => w.Days).ToList();
        Assert.Equal(DateOnly.FromDateTime(now), days.Last(d => !d.IsFuture).Date);
        Assert.True(days.Single(d => d.Date == DateOnly.FromDateTime(now.AddDays(-100))).Level >
                    days.Single(d => d.Date == DateOnly.FromDateTime(now.AddDays(-3))).Level);
        Assert.Equal(0, days.Single(d => d.Date == DateOnly.FromDateTime(now.AddDays(-1))).Level);
    }

    [Theory]
    [InlineData(812_000, "812k")]
    [InlineData(81_200_000, "81.2M")]
    [InlineData(3_000_800_000, "3.0B")]
    public void Tokens_AreShort(long tokens, string text) => Assert.Equal(text, StatisticsViewModel.FormatTokens(tokens));
}
