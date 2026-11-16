using KovanTerm.ViewModels;

namespace KovanTerm.Services;

/// <summary>What to do with a worktree when its node closes.</summary>
public enum WorktreeCloseChoice
{
    Cancel,
    /// <summary>Close the node; the worktree and branch stay.</summary>
    Keep,
    /// <summary>Close the node and delete the worktree folder and its branch.</summary>
    Delete,
}

/// <summary>Service provided by the view layer so ViewModels can open windows/dialogs.</summary>
public interface IDialogService
{
    /// <summary>Shows the "New terminal" dialog; <c>true</c> if the user confirms.</summary>
    bool ShowNewTerminal(NewTerminalViewModel viewModel);

    /// <summary>Lets the user pick a folder; <c>null</c> on cancel.</summary>
    string? PickFolder(string? initialDirectory);

    /// <summary>Asks whether to keep or delete a worktree whose node is closing.</summary>
    Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message);

    Task ShowErrorAsync(string title, string message);
}
