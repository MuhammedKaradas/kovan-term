namespace KovanTerm.Models;

/// <summary>
/// Yeni terminal başlatma şablonu (PowerShell, Claude Code, Codex…).
/// <see cref="Executable"/> PATH'te aranır; tam yol da verilebilir.
/// </summary>
public sealed record TerminalProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Executable { get; init; }
    public string Arguments { get; init; } = "";
    /// <summary>Düğüm başlığında gösterilecek vurgu rengi (#RRGGBB). Boşsa varsayılan.</summary>
    public string? Color { get; init; }
}
