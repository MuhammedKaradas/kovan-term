using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Cursor CLI and Antigravity nodes; a Cursor node keeps its chat.</summary>
public sealed class CursorChatsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public CursorChatsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55\r\n", "4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55")]
    [InlineData("  chat_01HZX9Q2  \n", "chat_01HZX9Q2")]
    [InlineData("Update available: 1.2.3\n4f9c2a1e-77b0\n", null)]
    [InlineData("Please run cursor-agent login\n", null)]
    [InlineData("", null)]
    public void ChatId_IsOneLineWithOnlyTheId(string output, string? id) => Assert.Equal(id, CursorChats.ParseChatId(output));

    [Fact]
    public void Resume_UnlessTheProfilePicksItsOwnChat()
    {
        Assert.Equal("--resume abc12345", CursorChats.Arguments("abc12345", ""));
        Assert.Equal("", CursorChats.Arguments("abc12345", "--resume other"));
        Assert.Equal("", CursorChats.Arguments("abc12345", "ls"));
        Assert.Equal("", CursorChats.Arguments("abc; del *", ""));
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\cursor-agent\cursor-agent.cmd", "cursor")]
    [InlineData(@"C:\tools\agy.exe", "antigravity")]
    public void BothAreAgents(string path, string kind) => Assert.Equal(kind, ScreenRules.AgentOfExecutable(path));

    [Fact]
    public void ProfilesAreThere() =>
        Assert.Equal(["cursor-agent", "agy"], ProfileStore.Defaults.Where(p => p.Id is "cursor" or "antigravity").Select(p => p.Executable));

    private MainViewModel Restore(params NodeState[] nodes)
    {
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = nodes.ToList() });
        var resolver = new CommandResolver(path: _dir, pathExt: ".cmd;.exe");
        return new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), new SettingsStore(Path.Combine(_dir, "settings.json")));
    }

    private static NodeState Cursor(string? chat) =>
        new() { ProfileId = "cursor", Title = "Cursor", CommandLine = "cursor-agent", SessionId = chat, Width = 400, Height = 300 };

    [Fact]
    public void AChatAlreadyMade_IsResumed()
    {
        File.WriteAllText(Path.Combine(_dir, "cursor-agent.cmd"), "@echo should-not-run");

        var node = Restore(Cursor("abc12345-chat")).Nodes.Single();

        Assert.False(node.IsStarting);
        Assert.Contains("--resume abc12345-chat", node.StartupCommandLine);
    }

    [Fact]
    public async Task WithoutAChat_OneIsMadeBeforeTheTerminalStarts()
    {
        // A stand-in for cursor-agent: create-chat prints an id
        File.WriteAllText(Path.Combine(_dir, "cursor-agent.cmd"), "@echo off\r\nif \"%1\"==\"create-chat\" echo 4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55\r\n");

        var node = Restore(Cursor(null)).Nodes.Single();
        Assert.True(node.IsStarting);
        Assert.Equal("Starting a Cursor chat…", node.StartingText);
        for (var i = 0; i < 100 && node.IsStarting; i++) await Task.Delay(100);

        Assert.False(node.IsStarting);
        Assert.Equal("4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55", node.SessionId);
        Assert.Contains("--resume 4f9c2a1e-77b0-4c1d-9a3e-2b8d6f0c1a55\"", node.StartupCommandLine);
    }

    [Fact]
    public async Task NoChat_StartsAnyway()
    {
        File.WriteAllText(Path.Combine(_dir, "cursor-agent.cmd"), "@echo off\r\necho Please run cursor-agent login\r\nexit /b 1\r\n");

        var node = Restore(Cursor(null)).Nodes.Single();
        for (var i = 0; i < 100 && node.IsStarting; i++) await Task.Delay(100);

        Assert.False(node.IsStarting);
        Assert.Null(node.SessionId);
        Assert.DoesNotContain("--resume", node.StartupCommandLine);
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
}
