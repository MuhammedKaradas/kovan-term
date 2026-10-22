using KovanTerm.ViewModels;

namespace KovanTerm.Services;

/// <summary>Service provided by the view layer so ViewModels can open windows/dialogs.</summary>
public interface IDialogService
{
    /// <summary>Shows the "New terminal" dialog; <c>true</c> if the user confirms.</summary>
    bool ShowNewTerminal(NewTerminalViewModel viewModel);

    /// <summary>Lets the user pick a folder; <c>null</c> on cancel.</summary>
    string? PickFolder(string? initialDirectory);
}
