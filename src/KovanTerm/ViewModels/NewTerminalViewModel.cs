using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>"Yeni terminal" diyaloğu: profil ve çalışma dizini seçimi.</summary>
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

    /// <summary>Diyalog kapanınca görünüm bunu izler: <c>true</c> onay, <c>false</c> iptal.</summary>
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
        SelectedProfile == null ? "Bir profil seç."
        : !SelectedProfile.IsAvailable ? SelectedProfile.Detail
        : string.IsNullOrWhiteSpace(WorkingDirectory) ? "Çalışma dizini seç."
        : !Directory.Exists(WorkingDirectory) ? "Bu klasör bulunamadı."
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
