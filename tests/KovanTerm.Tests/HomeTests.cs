using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.Services.Stats;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Home: the week's numbers, and the lists that follow the agents.</summary>
public sealed class HomeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    // A Wednesday afternoon
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0);

    public HomeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("2026-11-25", "2026-11-23")] // Wednesday → Monday
    [InlineData("2026-11-23", "2026-11-23")] // Monday
    [InlineData("2026-11-29", "2026-11-23")] // Sunday belongs to the week that started on Monday
    public void The_week_starts_on_monday(string day, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), HomeViewModel.WeekStart(DateTime.Parse(day)));

    private static WorkSession Session(string id, DateTime start, TimeSpan length, long tokens, DateOnly? tokenDay = null) =>
        new(id, "claude", @"C:\app", id, [], [new WorkSpan(start, start + length)],
            [new WorkTokenDay(tokenDay ?? DateOnly.FromDateTime(start), "opus", new WorkTokens(Input: tokens))]);

    [Fact]
    public void Today_and_the_week_count_time_tokens_and_sessions()
    {
        WorkSession[] sessions =
        [
            Session("today", Now.AddHours(-2), TimeSpan.FromHours(1), 1000),
            // Started before midnight: only today's part counts for today
            Session("overnight", Now.Date.AddMinutes(-30), TimeSpan.FromHours(1), 500, DateOnly.FromDateTime(Now)),
            Session("monday", new DateTime(2026, 11, 23, 10, 0, 0), TimeSpan.FromMinutes(45), 200),
            Session("last week", new DateTime(2026, 11, 20, 10, 0, 0), TimeSpan.FromHours(3), 9000),
        ];

        var today = HomeViewModel.Summarize(sessions, DateOnly.FromDateTime(Now), Now);
        Assert.Equal(TimeSpan.FromMinutes(90), today.Time);
        Assert.Equal(1500, today.Tokens);
        Assert.Equal(2, today.Sessions);
        Assert.Equal("1 h 30 m", today.TimeText);

        var week = HomeViewModel.Summarize(sessions, HomeViewModel.WeekStart(Now), Now);
        Assert.Equal(TimeSpan.FromMinutes(165), week.Time);
        Assert.Equal(1700, week.Tokens);
        Assert.Equal("3 sessions", week.SessionsText);
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => false;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string action) => Task.FromResult(false);
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(null);
    }

    [Fact]
    public async Task Lists_follow_the_agents()
    {
        NodeState Agent(string title) => new()
        {
            Id = Guid.NewGuid(), Title = title, CommandLine = "cmd", WorkingDirectory = _dir, Width = 400, Height = 300,
        };
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = [Agent("a"), Agent("b")] });
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        var main = new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), new SettingsStore(Path.Combine(_dir, "settings.json")));
        main.MeshBoard.CreateTask("Write the docs", null);
        var home = new HomeViewModel(main, () => [Session("s", Now.AddHours(-1), TimeSpan.FromMinutes(20), 10)], () => Now);

        Assert.True(home.NobodyWaits);
        Assert.Equal("Write the docs", Assert.Single(home.Tasks).Title);

        main.OnProgramStatus(main.Nodes[0], [new ProgramStatusReport("blocked", Kind: "permission")]);
        main.OnProgramStatus(main.Nodes[1], [new ProgramStatusReport("working")]);
        home.Refresh();
        Assert.Same(main.Nodes[0], Assert.Single(home.NeedsYou));
        Assert.Same(main.Nodes[1], Assert.Single(home.AtWork));
        Assert.False(home.NobodyWaits);

        TerminalNodeViewModel? went = null;
        home.GoToRequested += n => went = n;
        home.GoToCommand.Execute(main.Nodes[0]);
        Assert.Same(main.Nodes[0], went);

        await home.LoadStatsAsync();
        Assert.Equal(TimeSpan.FromMinutes(20), home.Today.Time);
    }
}
