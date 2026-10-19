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
        public string? PickFolder(string? initialDirectory) => folder;
    }

    private static ProfileOptionViewModel Option(string id, bool available) =>
        new(new TerminalProfile { Id = id, Name = id, Executable = id }, available ? $@"C:\bin\{id}.exe" : null);

    private static readonly string ExistingDir = Path.GetTempPath();

    [Fact]
    public void Baslangicta_TercihEdilenProfilVeSonDizinSecilir()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(),
            [Option("powershell", true), Option("claude", true)], [ExistingDir], preferredProfileId: "claude");

        Assert.Equal("claude", vm.SelectedProfile!.Profile.Id);
        Assert.Equal(ExistingDir, vm.WorkingDirectory);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void TercihEdilenProfilBulunamadiysa_IlkKullanilabilirSecilir()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(),
            [Option("codex", false), Option("cmd", true)], [], preferredProfileId: "codex");

        Assert.Equal("cmd", vm.SelectedProfile!.Profile.Id);
    }

    [Fact]
    public void BulunamayanProfilSecilince_OnaylanamazVeSebepGosterilir()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true), Option("codex", false)], [ExistingDir]);

        vm.SelectedProfile = vm.Profiles[1];

        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Contains("bulunamadı", vm.ValidationMessage);
    }

    [Fact]
    public void OlmayanDizinde_OnaylanamazVeSebepGosterilir()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true)], [ExistingDir]);

        vm.WorkingDirectory = Path.Combine(ExistingDir, Guid.NewGuid().ToString("N"));

        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal("Bu klasör bulunamadı.", vm.ValidationMessage);
    }

    [Fact]
    public void Gozat_SecilenKlasoruYazar_IptaldeDegistirmez()
    {
        var picked = new NewTerminalViewModel(new FakeDialogs(folder: @"C:\Secilen"), [Option("cmd", true)], [ExistingDir]);
        picked.BrowseCommand.Execute(null);
        Assert.Equal(@"C:\Secilen", picked.WorkingDirectory);

        var cancelled = new NewTerminalViewModel(new FakeDialogs(folder: null), [Option("cmd", true)], [ExistingDir]);
        cancelled.BrowseCommand.Execute(null);
        Assert.Equal(ExistingDir, cancelled.WorkingDirectory);
    }

    [Fact]
    public void OnayVeIptal_DialogResultuAyarlar()
    {
        var vm = new NewTerminalViewModel(new FakeDialogs(), [Option("cmd", true)], [ExistingDir]);
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogResult);

        vm.CancelCommand.Execute(null);
        Assert.False(vm.DialogResult);
    }
}
