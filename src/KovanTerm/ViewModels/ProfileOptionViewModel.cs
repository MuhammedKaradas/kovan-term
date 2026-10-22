using KovanTerm.Models;

namespace KovanTerm.ViewModels;

/// <summary>Profile listed in the dialog; can't be selected if its executable wasn't found.</summary>
public sealed class ProfileOptionViewModel(TerminalProfile profile, string? resolvedPath)
{
    public TerminalProfile Profile { get; } = profile;
    public string? ResolvedPath { get; } = resolvedPath;
    public bool IsAvailable => ResolvedPath != null;
    public string Name => Profile.Name;
    public string Detail => IsAvailable ? ResolvedPath! : $"'{Profile.Executable}' not found (not on PATH)";
}
