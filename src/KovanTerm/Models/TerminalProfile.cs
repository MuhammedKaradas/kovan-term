namespace KovanTerm.Models;

/// <summary>
/// Template for launching a new terminal (PowerShell, Claude Code, Codex…).
/// <see cref="Executable"/> is looked up on PATH; a full path can also be given.
/// </summary>
public sealed record TerminalProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>For WSL profiles a Linux command; empty opens the distro's login shell.</summary>
    public required string Executable { get; init; }
    public string Arguments { get; init; } = "";
    /// <summary>Accent color shown in the node header (#RRGGBB). Default if empty.</summary>
    public string? Color { get; init; }
    /// <summary>Runs inside WSL instead of on Windows.</summary>
    public bool Wsl { get; init; }
    /// <summary>WSL distro; the default distro if empty.</summary>
    public string? Distro { get; init; }
}
