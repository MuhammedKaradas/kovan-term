using System.IO;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>
/// Keeps a Claude Code node on the same conversation across restarts. The id is chosen up front
/// (<c>--session-id</c>) instead of being discovered afterwards; once Claude has saved the conversation,
/// later launches use <c>--resume</c>.
/// </summary>
public sealed partial class ClaudeSessions
{
    private readonly string _projectsDirectory;

    /// <param name="projectsDirectory">Where Claude keeps transcripts; defaults to <c>%CLAUDE_CONFIG_DIR%\projects</c> or <c>~\.claude\projects</c>.</param>
    public ClaudeSessions(string? projectsDirectory = null)
    {
        _projectsDirectory = projectsDirectory ?? DefaultProjectsDirectory;
    }

    /// <summary><c>%CLAUDE_CONFIG_DIR%\projects</c>, or <c>~\.claude\projects</c>.</summary>
    public static string DefaultProjectsDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
        "projects");

    // The user's profile already picks the conversation; don't add a second, conflicting choice
    [GeneratedRegex(@"(^|\s)(--resume|-r|--continue|-c|--session-id|--fork-session)(\s|=|$)")]
    private static partial Regex ConversationFlag();

    public static bool ChoosesConversation(string arguments) => ConversationFlag().IsMatch(arguments);

    /// <summary>Where Claude saved this conversation; <c>null</c> if it hasn't.</summary>
    public string? TranscriptOf(string sessionId)
    {
        if (!Directory.Exists(_projectsDirectory) || !Guid.TryParse(sessionId, out _)) return null;
        try
        {
            return Directory.EnumerateFiles(_projectsDirectory, sessionId + ".jsonl", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Has Claude saved this conversation (in any project folder)?</summary>
    public bool Exists(string sessionId)
    {
        if (!Directory.Exists(_projectsDirectory)) return false;
        try
        {
            return Directory.EnumerateFiles(_projectsDirectory, sessionId + ".jsonl", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Arguments that put the node on its conversation: <c>--resume</c> if it was saved, otherwise
    /// <c>--session-id</c> so the new conversation gets the node's id. Empty if the profile chooses itself.
    /// </summary>
    public string Arguments(string sessionId, string profileArguments)
    {
        if (ChoosesConversation(profileArguments)) return "";
        return Exists(sessionId) ? $"--resume {sessionId}" : $"--session-id {sessionId}";
    }

    /// <summary>
    /// The same inside WSL, where the transcripts are in the distro's <c>~/.claude</c>: the shell running the agent
    /// picks <c>--resume</c> or <c>--session-id</c> itself. The id is a GUID, so it needs no quoting.
    /// </summary>
    public static string WslArguments(string sessionId, string profileArguments)
    {
        if (ChoosesConversation(profileArguments) || !Guid.TryParse(sessionId, out _)) return "";
        return $"$(ls ~/.claude/projects/*/{sessionId}.jsonl >/dev/null 2>&1 && echo --resume || echo --session-id) {sessionId}";
    }
}
