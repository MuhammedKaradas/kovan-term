using System.Windows;
using KovanTerm.Services;
using KovanTerm.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

public sealed class DialogService(Window owner) : IDialogService
{
    public bool ShowNewTerminal(NewTerminalViewModel viewModel) =>
        new NewTerminalWindow(viewModel) { Owner = owner }.ShowDialog() == true;

    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = "Select working directory", InitialDirectory = initialDirectory ?? "" };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) =>
        Task.FromResult(ChoiceWindow.Ask(owner, title, message,
                ("Keep worktree", ControlAppearance.Primary),
                ("Delete worktree and branch", ControlAppearance.Danger),
                ("Cancel", ControlAppearance.Secondary)) switch
            {
                0 => WorktreeCloseChoice.Keep,
                1 => WorktreeCloseChoice.Delete,
                _ => WorktreeCloseChoice.Cancel,
            });

    public Task ShowErrorAsync(string title, string message)
    {
        ChoiceWindow.Ask(owner, title, message, ("OK", ControlAppearance.Primary));
        return Task.CompletedTask;
    }
}
