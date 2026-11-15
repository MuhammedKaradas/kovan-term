namespace KovanTerm.Services;

/// <summary>
/// Terminals inherit Kovan's environment. If Kovan was started from inside a Claude Code session, that session's
/// markers would leak into every agent: Claude then thinks it's nested, skips saving its transcript (so it can't
/// be resumed), and could even talk to the outer session's messaging socket. They're removed once at startup.
/// User settings such as <c>CLAUDE_CODE_GIT_BASH_PATH</c> or <c>ANTHROPIC_*</c> are kept.
/// </summary>
public static class AgentEnvironment
{
    public static IReadOnlyList<string> SessionMarkers { get; } =
    [
        "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN",
        "CLAUDE_CODE_EXECPATH", "CLAUDE_PID", "CLAUDE_EFFORT", "AI_AGENT",
    ];

    public static void RemoveSessionMarkers()
    {
        foreach (var name in SessionMarkers)
            Environment.SetEnvironmentVariable(name, null);
    }
}
