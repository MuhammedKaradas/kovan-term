namespace KovanTerm.Models;

public enum ThemePreference
{
    /// <summary>Follow the Windows light/dark setting.</summary>
    System,
    Dark,
    Light,
}

/// <summary>App-wide preferences (not tied to a workspace). Stored in <c>settings.json</c>.</summary>
public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    /// <summary>Is the Agents panel (left of the canvas) open?</summary>
    public bool ShowAgentsPanel { get; init; } = true;
    /// <summary>Is the Changes panel (right of the canvas) open?</summary>
    public bool ShowChangesPanel { get; init; }
    /// <summary>Command run in a new worktree before the agent starts, per repository root (e.g. <c>npm install</c>).</summary>
    public Dictionary<string, string> WorktreeSetupCommands { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Whisper model for dictation: <c>tiny</c>, <c>base</c> or <c>small</c> (more accurate, slower, bigger download).</summary>
    public string DictationModel { get; init; } = "base";
    /// <summary>Language spoken when dictating: a code such as <c>tr</c> or <c>en</c>, or <c>auto</c> to detect it.</summary>
    public string DictationLanguage { get; init; } = "auto";
}
