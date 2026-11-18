using KovanTerm.Models;

namespace KovanTerm.ViewModels;

/// <summary>
/// Profile listed in the dialog; can't be selected if its executable wasn't found. For WSL profiles
/// <see cref="ResolvedPath"/> is <c>wsl.exe</c>, found only if the distro is installed.
/// </summary>
public sealed class ProfileOptionViewModel(TerminalProfile profile, string? resolvedPath)
{
    public TerminalProfile Profile { get; } = profile;
    public string? ResolvedPath { get; } = resolvedPath;
    public bool IsAvailable => ResolvedPath != null;
    public string Name => Profile.Name;
    public string Detail => Profile.Wsl ? WslDetail
        : IsAvailable ? ResolvedPath! : $"'{Profile.Executable}' not found (not on PATH)";

    private string WslDetail
    {
        get
        {
            var distro = string.IsNullOrEmpty(Profile.Distro) ? "default distro" : Profile.Distro;
            if (!IsAvailable) return $"WSL distro '{distro}' not found";
            var command = string.IsNullOrWhiteSpace(Profile.Executable) ? "login shell" : Profile.Executable;
            return $"WSL · {distro} · {command}";
        }
    }
}
