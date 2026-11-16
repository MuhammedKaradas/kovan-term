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

    public Task ShowMessageAsync(string title, string message)
    {
        ChoiceWindow.Ask(owner, title, message, ("OK", ControlAppearance.Primary));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string action) =>
        Task.FromResult(ChoiceWindow.Ask(owner, title, message, (action, ControlAppearance.Primary), ("Cancel", ControlAppearance.Secondary)) == 0);

    public void OpenInBrowser(string url)
    {
        // Only web pages: the URL is built from the repository's remote
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
