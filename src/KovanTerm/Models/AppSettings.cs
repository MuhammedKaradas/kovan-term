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
}
