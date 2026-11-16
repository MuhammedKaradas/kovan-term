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
        _projectsDirectory = projectsDirectory ?? Path.Combine(
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } config
                ? config
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            "projects");
    }

    // The user's profile already picks the conversation; don't add a second, conflicting choice
    [GeneratedRegex(@"(^|\s)(--resume|-r|--continue|-c|--session-id|--fork-session)(\s|=|$)")]
    private static partial Regex ConversationFlag();

    public static bool ChoosesConversation(string arguments) => ConversationFlag().IsMatch(arguments);

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
}
