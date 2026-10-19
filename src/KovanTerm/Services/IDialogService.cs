using KovanTerm.ViewModels;

namespace KovanTerm.Services;

/// <summary>ViewModel'lerin pencere/diyalog açabilmesi için görünüm katmanının sağladığı servis.</summary>
public interface IDialogService
{
    /// <summary>"Yeni terminal" diyaloğunu gösterir; kullanıcı onaylarsa <c>true</c>.</summary>
    bool ShowNewTerminal(NewTerminalViewModel viewModel);

    /// <summary>Klasör seçtirir; iptalde <c>null</c>.</summary>
    string? PickFolder(string? initialDirectory);
}
