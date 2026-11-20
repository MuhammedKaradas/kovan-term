using System.IO;

namespace KovanTerm.Services;

/// <summary>
/// Qwen Code conversations: like Claude Code, a new one can be given its id up front (<c>--session-id</c>) and resumed
/// with <c>--resume</c>, so a node keeps its conversation across restarts. Qwen saves them as
/// <c>~/.qwen/projects/&lt;project&gt;/chats/&lt;id&gt;.jsonl</c> (<c>QWEN_HOME</c> moves <c>~/.qwen</c>).
/// </summary>
public sealed class QwenSessions(string? projectsDirectory = null)
{
    private readonly string _projectsDirectory = projectsDirectory ?? DefaultProjectsDirectory;

    public static string DefaultProjectsDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("QWEN_HOME") is { Length: > 0 } home ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qwen"),
        "projects");

    /// <summary>Has Qwen saved this conversation (in any project)?</summary>
    public bool Exists(string sessionId)
    {
        if (!Directory.Exists(_projectsDirectory)) return false;
        try
        {
            return Directory.EnumerateFiles(_projectsDirectory, sessionId + ".jsonl", SearchOption.AllDirectories)
                .Any(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "chats", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>--resume</c> if the conversation was saved, otherwise <c>--session-id</c>; empty when the profile picks one
    /// itself (Qwen refuses <c>--session-id</c> together with <c>--continue</c> or <c>--resume</c>).
    /// </summary>
    public string Arguments(string sessionId, string profileArguments)
    {
        var words = profileArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w is "-c" or "--continue" or "-r" or "--resume" or "--session-id" or "--fork-session"
            || w.StartsWith("--resume=", StringComparison.Ordinal) || w.StartsWith("--session-id=", StringComparison.Ordinal)))
            return "";
        return Exists(sessionId) ? $"--resume {sessionId}" : $"--session-id {sessionId}";
    }
}
