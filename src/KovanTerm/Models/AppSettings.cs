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
    /// <summary>Claude Code nodes take their conversation's title (its <c>/rename</c>, or the one Claude gives it).</summary>
    public bool TitlesFromClaude { get; init; } = true;
    /// <summary>Windows notification when an agent starts waiting for you while Kovan is in the background.</summary>
    public bool NotifyWhenWaiting { get; init; } = true;
    /// <summary>Windows notification when an agent's turn ends while Kovan is in the background.</summary>
    public bool NotifyWhenDone { get; init; } = true;
    /// <summary>Whisper model for dictation: <c>tiny</c>, <c>base</c> or <c>small</c> (more accurate, slower, bigger download).</summary>
    public string DictationModel { get; init; } = "base";
    /// <summary>Language spoken when dictating: a code such as <c>tr</c> or <c>en</c>, or <c>auto</c> to detect it.</summary>
    public string DictationLanguage { get; init; } = "auto";
    /// <summary>
    /// Agents start when Kovan opens; off, an agent node waits (showing its conversation) until it's started or sent a
    /// message. Plain shells always start.
    /// </summary>
    public bool StartAgentsOnOpen { get; init; } = true;
    /// <summary>Ask GitHub for a newer release when Kovan opens; it's only shown, nothing is downloaded.</summary>
    public bool CheckForUpdates { get; init; } = true;
    /// <summary>Ctrl+click on a web link in a terminal opens it in a web preview next to it, not in the default browser.</summary>
    public bool OpenWebLinksInPreview { get; init; }
    /// <summary>A small light on the screen's edge while Kovan is in the background: agents working and waiting.</summary>
    public bool StatusLight { get; init; }
    /// <summary>The status light's edge (right or left) and height on it, 0 (top) to 1 (bottom).</summary>
    public bool StatusLightRightEdge { get; init; } = true;
    public double StatusLightTop { get; init; } = 0.4;
    /// <summary>Windows doesn't go to sleep on its own while an agent is working.</summary>
    public bool KeepAwakeWhileWorking { get; init; } = true;
}
