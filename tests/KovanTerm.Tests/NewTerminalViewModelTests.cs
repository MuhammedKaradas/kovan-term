using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class NewTerminalViewModelTests
{
    private sealed class FakeDialogs(string? folder = null) : IDialogService
    {
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => true;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => folder;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Keep);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string action) => Task.FromResult(false);
        public void OpenInBrowser(string url) { }
    }

    private static ProfileOptionViewModel Option(string id, bool available) =>
        new(new TerminalProfile { Id = id, Name = id, Executable = id }, available ? $@"C:\bin\{id}.exe" : null);

    private static readonly string ExistingDir = Path.GetTempPath();

    private static ProfileOptionViewModel WslOption(string id, string? distro = null) =>
        new(new TerminalProfile { Id = id, Name = id, Executable = "", Wsl = true, Distro = distro }, @"C:\Windows\System32\wsl.exe");

    [Fact]
    public void Initially_SelectsPreferredProfileAndLastDirectory()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(),
            [Option("powershell", true), Option("claude", true)], [ExistingDir], preferredProfileId: "claude");

        Assert.Equal("claude", vm.SelectedProfile!.Profile.Id);
        Assert.Equal(ExistingDir, vm.WorkingDirectory);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void PreferredProfileUnavailable_SelectsFirstAvailable()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(),
            [Option("codex", false), Option("cmd", true)], [], preferredProfileId: "codex");

        Assert.Equal("cmd", vm.SelectedProfile!.Profile.Id);
    }

    [Fact]
    public void UnavailableProfileSelected_CannotConfirmAndShowsReason()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true), Option("codex", false)], [ExistingDir]);

        vm.SelectedProfile = vm.Profiles[1];

        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Contains("not found", vm.ValidationMessage);
    }

    [Fact]
    public void MissingDirectory_CannotConfirmAndShowsReason()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true)], [ExistingDir]);

        vm.WorkingDirectory = Path.Combine(ExistingDir, Guid.NewGuid().ToString("N"));

        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal("This folder doesn't exist.", vm.ValidationMessage);
    }

    [Fact]
    public void Browse_SetsPickedFolder_UnchangedOnCancel()
    {
        var picked = new NewTerminalViewModel(new FakeDialogs(folder: @"C:\Picked"), [Option("cmd", true)], [ExistingDir]);
        picked.BrowseCommand.Execute(null);
        Assert.Equal(@"C:\Picked", picked.WorkingDirectory);

        var cancelled = new NewTerminalViewModel(new FakeDialogs(folder: null), [Option("cmd", true)], [ExistingDir]);
        cancelled.BrowseCommand.Execute(null);
        Assert.Equal(ExistingDir, cancelled.WorkingDirectory);
    }

    [Fact]
    public void SwitchingToWsl_UsesRecentLinuxDirectory_AndBack()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true), WslOption("wsl")], [ExistingDir, "/srv/app"]);
        Assert.Equal(ExistingDir, vm.WorkingDirectory);

        vm.SelectedProfile = vm.Profiles[1];
        Assert.Equal("/srv/app", vm.WorkingDirectory);
        Assert.Null(vm.ValidationMessage); // default distro: the path can't be checked

        vm.SelectedProfile = vm.Profiles[0];
        Assert.Equal(ExistingDir, vm.WorkingDirectory);
    }

    [Fact]
    public void SwitchingToWsl_WithoutRecentLinuxDirectory_UsesHome()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true), WslOption("wsl", "Debian")], [ExistingDir]);

        vm.SelectedProfile = vm.Profiles[1];

        Assert.Equal("~", vm.WorkingDirectory);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void LinuxPath_WithWindowsProfile_CannotConfirm()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true)], [ExistingDir]);

        vm.WorkingDirectory = "/srv/app";

        Assert.Equal("Linux paths only work with WSL profiles.", vm.ValidationMessage);
    }

    [Fact]
    public void WslProfile_AcceptsWindowsDirectory()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [WslOption("wsl", "Debian")], ["~"]);

        vm.WorkingDirectory = ExistingDir;

        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public void Browse_InWsl_ConvertsDistroFolderToLinuxPath()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(folder: @"\\wsl.localhost\Debian\srv\app"), [WslOption("wsl", "Debian")], ["~"]);

        vm.BrowseCommand.Execute(null);

        Assert.Equal("/srv/app", vm.WorkingDirectory);
    }

    [Fact]
    public void ConfirmAndCancel_SetDialogResult()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true)], [ExistingDir]);
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogResult);

        vm.CancelCommand.Execute(null);
        Assert.False(vm.DialogResult);
    }
}
