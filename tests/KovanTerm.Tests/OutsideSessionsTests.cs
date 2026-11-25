using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Claude Code sessions started outside Kovan: the hook in the user's settings, the launcher's hook mode, the panel rows.</summary>
public sealed class OutsideSessionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private const string Command = "\"C:/Kovan/KovanLaunch.exe\" --hook \"C:/Data/outside.endpoint\"";
    private static string Launcher => Path.Combine(AppContext.BaseDirectory, "KovanLaunch.exe");

    public OutsideSessionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private OutsideHook Hook(string launcher = @"C:\Kovan\KovanLaunch.exe") =>
        new(Path.Combine(_dir, ".claude", "settings.json"), Path.Combine(_dir, "outside.endpoint"), launcher);

    private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    [Fact]
    public void Adds_a_background_command_hook_for_every_event()
    {
        var hooks = Json(OutsideHook.Add(null, Command))["hooks"]!.AsObject();

        Assert.Equal(OutsideHook.Events.Order(), hooks.Select(h => h.Key).Order());
        foreach (var name in OutsideHook.Events)
        {
            var hook = hooks[name]![0]!["hooks"]![0]!;
            Assert.Equal("command", (string?)hook["type"]);
            Assert.Equal(Command, (string?)hook["command"]);
            Assert.True((bool)hook["async"]!);
        }
    }

    [Fact]
    public void Keeps_the_users_own_settings_and_hooks()
    {
        const string mine = """
            {
              "model": "opus",
              "statusLine": { "type": "command", "command": "echo çalışıyor <ok> +1" },
              "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "notify.exe" } ] } ] }
            }
            """;

        var added = OutsideHook.Add(mine, Command);
        var root = Json(added);

        Assert.Equal("opus", (string?)root["model"]);
        Assert.Contains("echo çalışıyor <ok> +1", added);
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("notify.exe", (string?)stop[0]!["hooks"]![0]!["command"]);

        // Taken out again: the user's file as it was
        var removed = Json(OutsideHook.Remove(added)!);
        Assert.True(JsonNode.DeepEquals(Json(mine), removed));
    }

    [Fact]
    public void Adding_again_replaces_an_older_Kovan_hook()
    {
        var old = OutsideHook.Add(null, "\"D:/Old/KovanLaunch.exe\" --hook \"D:/Old/outside.endpoint\"");

        var hooks = Json(OutsideHook.Add(old, Command))["hooks"]!.AsObject();

        foreach (var name in OutsideHook.Events)
            Assert.Equal(Command, (string?)Assert.Single(hooks[name]!.AsArray())!["hooks"]![0]!["command"]);
    }

    [Fact]
    public void Removing_when_there_is_no_Kovan_hook_changes_nothing()
    {
        Assert.Null(OutsideHook.Remove("""{ "hooks": { "Stop": [] } }"""));
        Assert.Null(OutsideHook.Remove(null));
    }

    [Fact]
    public void Install_and_uninstall_the_users_settings_file()
    {
        var hook = Hook();
        Directory.CreateDirectory(Path.GetDirectoryName(hook.SettingsPath)!);
        File.WriteAllText(hook.SettingsPath, """{ "model": "opus" }""");
        Assert.False(hook.IsInstalled());

        hook.Install();
        Assert.True(hook.IsInstalled());
        Assert.Contains("C:/Kovan/KovanLaunch.exe", File.ReadAllText(hook.SettingsPath));

        // Kovan moved: the hook follows it
        var moved = Hook(@"E:\Apps\Kovan\KovanLaunch.exe");
        Assert.False(moved.IsInstalled());
        moved.Install();
        Assert.True(moved.IsInstalled());
        Assert.DoesNotContain("C:/Kovan", File.ReadAllText(hook.SettingsPath));

        moved.WriteEndpoint("http://127.0.0.1:1/outside", "t");
        moved.Uninstall();
        Assert.False(moved.IsInstalled());
        Assert.True(JsonNode.DeepEquals(Json("""{ "model": "opus" }"""), Json(File.ReadAllText(hook.SettingsPath))));
        Assert.False(File.Exists(moved.EndpointPath));
    }

    [Fact]
    public void A_settings_file_that_isnt_JSON_is_left_alone()
    {
        var hook = Hook();
        Directory.CreateDirectory(Path.GetDirectoryName(hook.SettingsPath)!);
        File.WriteAllText(hook.SettingsPath, "{ not json");

        Assert.ThrowsAny<JsonException>(hook.Install);
        Assert.Equal("{ not json", File.ReadAllText(hook.SettingsPath));
    }

    [Fact]
    public void Server_reads_the_working_directory()
    {
        var e = AgentHookServer.ParseEvent("""{"hook_event_name":"Stop","session_id":"s","cwd":"C:\\work\\app"}"""u8);

        Assert.Equal(@"C:\work\app", e!.Cwd);
    }

    [Fact]
    public async Task The_launchers_hook_sends_the_event_to_Kovan()
    {
        using var server = new AgentHookServer();
        var received = new TaskCompletionSource<AgentEvent>();
        server.OutsideEventReceived += e => received.TrySetResult(e);
        var hook = Hook(Launcher);
        hook.WriteEndpoint(server.OutsideUrl, server.Token);

        var code = RunHook(hook.EndpointPath, """{"hook_event_name":"PermissionRequest","session_id":"s1","tool_name":"Bash","cwd":"C:\\app"}""");

        Assert.Equal(0, code);
        var e = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new AgentEvent("PermissionRequest", "s1", "Bash", Cwd: @"C:\app"), e);
    }

    [Fact]
    public async Task Events_with_a_wrong_token_are_dropped()
    {
        using var server = new AgentHookServer();
        var received = false;
        server.OutsideEventReceived += _ => received = true;
        var hook = Hook(Launcher);
        hook.WriteEndpoint(server.OutsideUrl, "not-the-token");

        Assert.Equal(0, RunHook(hook.EndpointPath, """{"hook_event_name":"Stop","session_id":"s1"}"""));
        await Task.Delay(200);
        Assert.False(received);
    }

    [Fact]
    public void With_Kovan_closed_the_hook_returns_at_once()
    {
        var watch = Stopwatch.StartNew();

        Assert.Equal(0, RunHook(Path.Combine(_dir, "missing.endpoint"), """{"hook_event_name":"Stop"}"""));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
    }

    private static int RunHook(string endpointFile, string input)
    {
        var psi = new ProcessStartInfo(Launcher) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add("--hook");
        psi.ArgumentList.Add(endpointFile);
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(input);
        p.StandardInput.Close();
        Assert.Equal("", p.StandardOutput.ReadToEnd());
        p.WaitForExit(10_000);
        return p.ExitCode;
    }

    // ---- The panel ----

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

    private MainViewModel Vm(AgentHookServer server, bool on = true, params NodeState[] nodes)
    {
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = nodes.ToList() });
        var settings = new SettingsStore(Path.Combine(_dir, "settings.json"));
        settings.Save(settings.Load() with { ShowOutsideSessions = on });
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        return new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), settings, server, outsideHook: Hook());
    }

    [Fact]
    public void Outside_sessions_show_until_they_end()
    {
        using var server = new AgentHookServer();
        var vm = Vm(server);
        var waiting = new List<OutsideSessionViewModel>();
        vm.OutsideAttentionNeeded += waiting.Add;

        vm.OnOutsideEvent(new AgentEvent("SessionStart", "s1", Cwd: @"C:\work\shop"));
        var row = Assert.Single(vm.OutsideSessions);
        Assert.Equal("shop", row.Title);
        Assert.True(vm.HasOutsideSessions);

        vm.OnOutsideEvent(new AgentEvent("PermissionRequest", "s1", "Bash"));
        Assert.Equal(NodeStatus.Waiting, row.Status);
        Assert.Equal("Needs you · Bash", row.StatusText);
        Assert.Same(row, Assert.Single(waiting));

        vm.OnOutsideEvent(new AgentEvent("PostToolUse", "s1", "Bash"));
        Assert.Equal(NodeStatus.Working, row.Status);

        vm.OnOutsideEvent(new AgentEvent("SessionEnd", "s1"));
        Assert.Empty(vm.OutsideSessions);
        Assert.False(vm.HasOutsideSessions);
    }

    [Fact]
    public void Sessions_Kovan_started_arent_shown_as_outside()
    {
        using var server = new AgentHookServer();
        var vm = Vm(server, on: true, new NodeState
        {
            Id = Guid.NewGuid(), Title = "Claude", CommandLine = "claude", WorkingDirectory = _dir, SessionId = "mine",
            Width = 400, Height = 300,
        });

        vm.OnOutsideEvent(new AgentEvent("UserPromptSubmit", "mine", Cwd: _dir));

        Assert.Empty(vm.OutsideSessions);
    }

    [Fact]
    public void Nothing_is_shown_while_the_setting_is_off()
    {
        using var server = new AgentHookServer();
        var vm = Vm(server, on: false);

        vm.OnOutsideEvent(new AgentEvent("SessionStart", "s1", Cwd: _dir));

        Assert.Empty(vm.OutsideSessions);
    }

    [Fact]
    public void Turning_it_on_adds_the_hook_and_off_takes_it_out()
    {
        using var server = new AgentHookServer();
        var vm = Vm(server, on: false);
        var hook = Hook();

        Assert.Null(vm.SetOutsideSessions(true));
        Assert.True(hook.IsInstalled());
        Assert.True(vm.Settings.ShowOutsideSessions);
        Assert.Equal($"{server.OutsideUrl}\n{server.Token}\n", File.ReadAllText(hook.EndpointPath));

        Assert.Null(vm.SetOutsideSessions(false));
        Assert.False(hook.IsInstalled());
        Assert.False(vm.Settings.ShowOutsideSessions);
    }

    [Fact]
    public void A_broken_settings_file_is_reported_and_the_setting_stays_off()
    {
        using var server = new AgentHookServer();
        var vm = Vm(server, on: false);
        var hook = Hook();
        Directory.CreateDirectory(Path.GetDirectoryName(hook.SettingsPath)!);
        File.WriteAllText(hook.SettingsPath, "{ not json");

        var error = vm.SetOutsideSessions(true);

        Assert.Contains("isn't valid JSON", error);
        Assert.False(vm.Settings.ShowOutsideSessions);
    }
}
