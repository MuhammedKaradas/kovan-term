namespace KovanTerm.Models;

/// <summary>
/// Template for launching a new terminal (PowerShell, Claude Code, Codex…).
/// <see cref="Executable"/> is looked up on PATH; a full path can also be given.
/// </summary>
public sealed record TerminalProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Executable { get; init; }
    public string Arguments { get; init; } = "";
    /// <summary>Accent color shown in the node header (#RRGGBB). Default if empty.</summary>
    public string? Color { get; init; }
}
