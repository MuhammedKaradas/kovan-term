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

    /// <summary>Shows the "Recent conversations" dialog; <c>true</c> if the user picks one to open.</summary>
    bool ShowRecentConversations(RecentConversationsViewModel viewModel);

    /// <summary>Lets the user pick a folder; <c>null</c> on cancel.</summary>
    string? PickFolder(string? initialDirectory);

    /// <summary>Asks whether to keep or delete a worktree whose node is closing.</summary>
    Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message);

    /// <summary>
    /// Asks the user to pick one of <paramref name="buttons"/> (the first is the primary one, a button named "Cancel"
    /// the secondary one); the index of the picked button, or <c>null</c> if the dialog was closed.
    /// </summary>
    Task<int?> ChooseAsync(string title, string message, params string[] buttons);

    /// <summary>Shows a message with an OK button (errors and results).</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Asks to confirm an action; <c>true</c> if the user presses the action's button.</summary>
    Task<bool> ConfirmAsync(string title, string message, string action);

    /// <summary>Opens a web page in the default browser.</summary>
    void OpenInBrowser(string url);

    /// <summary>Shows "Send to agents"; <c>true</c> if the user sends.</summary>
    bool ShowBroadcast(BroadcastViewModel viewModel) => false;
}
