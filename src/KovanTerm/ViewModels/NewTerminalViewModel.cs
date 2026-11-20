using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>"New terminal" dialog: profile and working directory selection.</summary>
public partial class NewTerminalViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
    public IReadOnlyList<string> RecentDirectories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial ProfileOptionViewModel? SelectedProfile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string WorkingDirectory { get; set; }

    /// <summary>The view watches this to close the dialog: <c>true</c> confirm, <c>false</c> cancel.</summary>
    [ObservableProperty]
    public partial bool? DialogResult { get; set; }

    public NewTerminalViewModel(IDialogService dialogs, IReadOnlyList<ProfileOptionViewModel> profiles,
        IReadOnlyList<string> recentDirectories, string? preferredProfileId = null)
    {
        _dialogs = dialogs;
        Profiles = profiles;
        RecentDirectories = recentDirectories;
        WorkingDirectory = recentDirectories.FirstOrDefault()
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SelectedProfile = profiles.FirstOrDefault(p => p.Profile.Id == preferredProfileId && p.IsAvailable)
            ?? profiles.FirstOrDefault(p => p.IsAvailable);
    }

    public bool IsWsl => SelectedProfile?.Profile.Wsl == true;

    /// <summary>WSL: run in a hidden tmux session that outlives Kovan (see <see cref="TerminalProfile.Persistent"/>).</summary>
    [ObservableProperty]
    public partial bool KeepRunning { get; set; }
    private string? Distro => SelectedProfile?.Profile.Distro is { Length: > 0 } d ? d : null;

    public string? ValidationMessage =>
        SelectedProfile == null ? "Select a profile."
        : !SelectedProfile.IsAvailable ? SelectedProfile.Detail
        : string.IsNullOrWhiteSpace(WorkingDirectory) ? "Select a working directory."
        : !IsWsl && WslLauncher.IsLinuxPath(WorkingDirectory) ? "Linux paths only work with WSL profiles."
        : !DirectoryExists(WorkingDirectory) ? "This folder doesn't exist."
        : UseWorktree && CanUseWorktree && !GitWorktrees.IsValidBranch(BranchName)
            ? "Branch names can use letters, digits, '.', '_', '-' and '/'."
        : PicksModel && string.IsNullOrWhiteSpace(Model) ? "Pick a model."
        : null;

    // ---- Model (Ollama) ----

    /// <summary>Lists a model source's models; <c>null</c> when it isn't answering. Set by whoever creates the dialog.</summary>
    public Func<string, Task<IReadOnlyList<string>?>>? ListModels { get; init; }

    /// <summary>The profile runs a model picked here (<see cref="Models.TerminalProfile.ModelSource"/>).</summary>
    public bool PicksModel => SelectedProfile?.Profile.ModelSource != null;

    [ObservableProperty]
    public partial IReadOnlyList<string> ModelOptions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string Model { get; set; } = "";

    /// <summary>Under the model box: loading, nothing installed, or Ollama not running.</summary>
    [ObservableProperty]
    public partial string? ModelStatus { get; private set; }

    /// <summary>The node's title with the model in it, e.g. "Claude Code (Ollama · qwen3-coder)"; <c>null</c> otherwise.</summary>
    public string? ModelTitle => PicksModel && !string.IsNullOrWhiteSpace(Model) && SelectedProfile is { } p
        ? (p.Name.EndsWith(')') ? $"{p.Name[..^1]} · {Model.Trim()})" : $"{p.Name} ({Model.Trim()})")
        : null;

    private int _modelLoad;

    /// <summary>Lists the selected profile's models; the last call wins when the profile changes meanwhile.</summary>
    public async Task LoadModelsAsync()
    {
        var load = ++_modelLoad;
        if (SelectedProfile?.Profile.ModelSource is not { } source || ListModels == null)
        {
            ModelOptions = [];
            ModelStatus = null;
            return;
        }
        ModelStatus = "Looking for installed models…";
        var models = await ListModels(source);
        if (load != _modelLoad) return;
        ModelOptions = models ?? [];
        ModelStatus = models == null ? $"Ollama isn't answering at {OllamaModels.DefaultAddress}. Start Ollama (or install it from ollama.com), then reopen this dialog."
            : models.Count == 0 ? "No models installed yet. Get one with: ollama pull qwen3-coder"
            : "Small local models struggle with agent work; pick a larger one with a 64k+ context if you can.";
        if (string.IsNullOrWhiteSpace(Model) && ModelOptions.Count > 0) Model = ModelOptions[0];
    }

    // ---- Worktree ----

    /// <summary>The git repository the working directory is in; <c>null</c> if it isn't in one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseWorktree), nameof(WorktreePathPreview))]
    public partial string? RepoRoot { get; private set; }

    public bool CanUseWorktree => RepoRoot != null;

    /// <summary>Start the agent in a new worktree on its own branch, so it doesn't touch other agents' files.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage), nameof(WorktreePathPreview))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool UseWorktree { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage), nameof(WorktreePathPreview))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string BranchName { get; set; } = "";

    public string? WorktreePathPreview =>
        RepoRoot != null && GitWorktrees.IsValidBranch(BranchName) ? GitWorktrees.PathFor(RepoRoot, BranchName) : null;

    /// <summary>
    /// Command run in the new worktree before the agent starts (e.g. <c>npm install</c>); remembered per repository.
    /// </summary>
    [ObservableProperty]
    public partial string SetupCommand { get; set; } = "";

    /// <summary>Gives the remembered setup command of a repository; set by whoever creates the dialog.</summary>
    public Func<string, string?>? SetupCommandFor { get; init; }

    partial void OnRepoRootChanged(string? value) =>
        SetupCommand = value != null ? SetupCommandFor?.Invoke(value) ?? "" : "";

    /// <summary>Is a worktree going to be created? (The option can be on while the folder isn't a repository.)</summary>
    public bool CreatesWorktree => UseWorktree && CanUseWorktree;

    private bool _branchEdited;

    partial void OnWorkingDirectoryChanged(string value) =>
        RepoRoot = Directory.Exists(value) ? GitWorktrees.FindRepoRoot(value) : null;

    partial void OnBranchNameChanged(string value) => _branchEdited = true;


    /// <summary>
    /// For WSL a Linux path is checked through <c>\\wsl.localhost</c>; with the default distro its name is unknown,
    /// so it isn't checked. Windows paths also work in WSL (as <c>/mnt/c/…</c>).
    /// </summary>
    private bool DirectoryExists(string directory)
    {
        if (!IsWsl || !WslLauncher.IsLinuxPath(directory)) return Directory.Exists(directory);
        if (directory == WslLauncher.HomeDirectory || Distro == null) return true;
        return Directory.Exists(WslLauncher.ToUncPath(Distro, directory));
    }

    /// <summary>When switching between Windows and WSL profiles, swaps in a directory of the matching kind.</summary>
    partial void OnSelectedProfileChanged(ProfileOptionViewModel? value)
    {
        OnPropertyChanged(nameof(IsWsl));
        OnPropertyChanged(nameof(PicksModel));
        OnPropertyChanged(nameof(ValidationMessage));
        ConfirmCommand.NotifyCanExecuteChanged();
        _ = LoadModelsAsync();
        KeepRunning = value?.Profile is { Wsl: true, Persistent: true };
        // Keep the branch suggestion in step with the profile until the user types their own name
        if (!_branchEdited && value != null)
        {
            BranchName = GitWorktrees.SuggestBranch(value.Profile.Id);
            _branchEdited = false;
        }
        if (value == null || string.IsNullOrWhiteSpace(WorkingDirectory)) return;
        var wantLinux = value.Profile.Wsl;
        if (WslLauncher.IsLinuxPath(WorkingDirectory) == wantLinux) return;
        if (!wantLinux)
        {
            WorkingDirectory = RecentDirectories.FirstOrDefault(d => !WslLauncher.IsLinuxPath(d))
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else
        {
            WorkingDirectory = RecentDirectories.FirstOrDefault(WslLauncher.IsLinuxPath) ?? WslLauncher.HomeDirectory;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => DialogResult = true;

    private bool CanConfirm() => ValidationMessage == null;

    [RelayCommand]
    private void Cancel() => DialogResult = false;

    [RelayCommand]
    private void Browse()
    {
        // In WSL, the distro's files are browsed through \\wsl.localhost and the result turned back into a Linux path
        var current = IsWsl && Distro != null && WorkingDirectory.StartsWith('/')
            ? WslLauncher.ToUncPath(Distro, WorkingDirectory)
            : WorkingDirectory;
        if (_dialogs.PickFolder(Directory.Exists(current) ? current : null) is not { } folder) return;
        WorkingDirectory = IsWsl && Distro != null ? WslLauncher.FromUncPath(folder, Distro) ?? folder : folder;
    }
}
