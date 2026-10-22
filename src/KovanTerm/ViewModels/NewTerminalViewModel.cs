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

    public string? ValidationMessage =>
        SelectedProfile == null ? "Select a profile."
        : !SelectedProfile.IsAvailable ? SelectedProfile.Detail
        : string.IsNullOrWhiteSpace(WorkingDirectory) ? "Select a working directory."
        : !Directory.Exists(WorkingDirectory) ? "This folder doesn't exist."
        : null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => DialogResult = true;

    private bool CanConfirm() => ValidationMessage == null;

    [RelayCommand]
    private void Cancel() => DialogResult = false;

    [RelayCommand]
    private void Browse()
    {
        if (_dialogs.PickFolder(Directory.Exists(WorkingDirectory) ? WorkingDirectory : null) is { } folder)
            WorkingDirectory = folder;
    }
}
