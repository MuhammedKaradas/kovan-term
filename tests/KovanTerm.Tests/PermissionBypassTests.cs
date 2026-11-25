using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Claude Code without permission prompts: everywhere or in trusted folders, never over a profile's own mode.</summary>
public sealed class PermissionBypassTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private string Trusted => Path.Combine(_dir, "trusted");
    private string Other => Path.Combine(_dir, "other");

    public PermissionBypassTests()
    {
        Directory.CreateDirectory(Path.Combine(Trusted, "sub"));
        Directory.CreateDirectory(Other);
        Directory.CreateDirectory(Path.Combine(_dir, "bin"));
        File.WriteAllText(Path.Combine(_dir, "bin", "claude.exe"), "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Applies_in_listed_folders_and_below()
    {
        string[] folders = [Trusted];
        Assert.True(PermissionBypass.AppliesTo(false, folders, Trusted));
        Assert.True(PermissionBypass.AppliesTo(false, folders, Trusted + Path.DirectorySeparatorChar));
        Assert.True(PermissionBypass.AppliesTo(false, folders, Path.Combine(Trusted, "sub")));
        // A worktree elsewhere, of a trusted repository
        Assert.True(PermissionBypass.AppliesTo(false, folders, Path.Combine(_dir, "wt"), repoRoot: Trusted));
        Assert.False(PermissionBypass.AppliesTo(false, folders, Other));
        // "trusted-2" isn't inside "trusted"
        Assert.False(PermissionBypass.AppliesTo(false, folders, Trusted + "-2"));
        Assert.True(PermissionBypass.AppliesTo(true, [], Other));
        Assert.True(PermissionBypass.AppliesTo(false, ["/home/me/app"], "/home/me/app/src"));
    }

    [Fact]
    public void A_profile_s_own_mode_wins()
    {
        Assert.True(PermissionBypass.NamesPermissionMode("--permission-mode plan"));
        Assert.True(PermissionBypass.NamesPermissionMode("--dangerously-skip-permissions"));
        Assert.False(PermissionBypass.NamesPermissionMode("--model opus"));
    }

    [Fact]
    public void Folders_are_added_and_taken_out_once()
    {
        var list = PermissionBypass.With([], Trusted, on: true);
        list = PermissionBypass.With(list, Trusted + Path.DirectorySeparatorChar, on: true);
        Assert.Single(list);
        Assert.Empty(PermissionBypass.With(list, Trusted.ToUpperInvariant(), on: false));
    }

    private MainViewModel Restore(AppSettings settings, string profileArguments, params NodeState[] nodes)
    {
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        store.Save(new WorkspaceState { Nodes = nodes.ToList() });
        var settingsStore = new SettingsStore(Path.Combine(_dir, "settings.json"));
        settingsStore.Save(settings);
        if (profileArguments.Length > 0)
            File.WriteAllText(Path.Combine(_dir, "profiles.json"),
                $$"""[{ "id": "claude", "name": "Claude Code", "executable": "claude", "arguments": "{{profileArguments}}" }]""");
        var resolver = new CommandResolver(path: Path.Combine(_dir, "bin"), pathExt: ".exe");
        return new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), new NoDialogs(), settingsStore);
    }

    private static NodeState Claude(string folder) => new()
    {
        Id = Guid.NewGuid(), ProfileId = "claude", Title = "Claude Code", CommandLine = "claude", WorkingDirectory = folder,
        Width = 400, Height = 300,
    };

    [Fact]
    public void A_trusted_folder_starts_without_prompts_and_others_dont()
    {
        var vm = Restore(new AppSettings { SkipPermissionsFolders = [Trusted] }, "", Claude(Trusted), Claude(Other));

        Assert.Contains(PermissionBypass.Arguments, vm.Nodes[0].StartupCommandLine);
        Assert.True(vm.Nodes[0].SkipsPermissions);
        Assert.DoesNotContain("bypassPermissions", vm.Nodes[1].StartupCommandLine);
        Assert.False(vm.Nodes[1].SkipsPermissions);
    }

    [Fact]
    public void Everywhere_from_settings()
    {
        var vm = Restore(new AppSettings { SkipPermissionsEverywhere = true }, "", Claude(Other));

        Assert.Contains(PermissionBypass.Arguments, vm.Nodes[0].StartupCommandLine);
    }

    [Fact]
    public void A_profile_with_its_own_permission_mode_is_left_alone()
    {
        var vm = Restore(new AppSettings { SkipPermissionsEverywhere = true }, "--permission-mode plan", Claude(Other));

        Assert.Contains("--permission-mode plan", vm.Nodes[0].StartupCommandLine);
        Assert.DoesNotContain("bypassPermissions", vm.Nodes[0].StartupCommandLine);
    }

    [Fact]
    public void The_dialog_ticks_the_box_for_a_trusted_folder()
    {
        var claude = new ProfileOptionViewModel(new TerminalProfile { Id = "claude", Name = "Claude Code", Executable = "claude" }, "claude.exe");
        var dialog = new NewTerminalViewModel(new NoDialogs(), [claude], [Other], "claude")
        {
            SkipPermissionsFor = f => PermissionBypass.AppliesTo(false, [Trusted], f),
        };
        dialog.LookUpSkipPermissions();
        Assert.True(dialog.ShowsSkipPermissions);
        Assert.False(dialog.SkipPermissions);

        dialog.WorkingDirectory = Path.Combine(Trusted, "sub");
        Assert.True(dialog.SkipPermissions);
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
