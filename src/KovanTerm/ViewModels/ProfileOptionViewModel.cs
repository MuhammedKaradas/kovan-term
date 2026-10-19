using KovanTerm.Models;

namespace KovanTerm.ViewModels;

/// <summary>Diyalogda listelenen profil; çalıştırılabilir dosya bulunamadıysa seçilemez.</summary>
public sealed class ProfileOptionViewModel(TerminalProfile profile, string? resolvedPath)
{
    public TerminalProfile Profile { get; } = profile;
    public string? ResolvedPath { get; } = resolvedPath;
    public bool IsAvailable => ResolvedPath != null;
    public string Name => Profile.Name;
    public string Detail => IsAvailable ? ResolvedPath! : $"'{Profile.Executable}' bulunamadı (PATH'te yok)";
}
