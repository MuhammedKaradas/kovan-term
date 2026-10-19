using System.Windows;
using KovanTerm.Services;
using KovanTerm.ViewModels;
using Microsoft.Win32;

namespace KovanTerm.Views;

public sealed class DialogService(Window owner) : IDialogService
{
    public bool ShowNewTerminal(NewTerminalViewModel viewModel) =>
        new NewTerminalWindow(viewModel) { Owner = owner }.ShowDialog() == true;

    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = "Çalışma dizini seç", InitialDirectory = initialDirectory ?? "" };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
}
