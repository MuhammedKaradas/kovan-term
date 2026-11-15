using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>What a node shows about its agent: status, a short detail and whether Allow/Deny can answer it.</summary>
public sealed record AgentStatus(NodeStatus Status, string? Detail = null, bool AwaitsPermission = false)
{
    public static AgentStatus Idle { get; } = new(NodeStatus.Idle);
}

/// <summary>Turns agent events into the status shown on the node. Pure, so it's unit tested.</summary>
public static class AgentStatusReducer
{
    // Tools whose "permission" prompt isn't a yes/no question: pressing "1" would pick an answer or approve a plan
    private static readonly HashSet<string> NotYesNoTools = new(StringComparer.Ordinal) { "AskUserQuestion", "ExitPlanMode" };

    public static AgentStatus Apply(AgentStatus current, AgentEvent e) => e.Name switch
    {
        "UserPromptSubmit" or "SubagentStart" or "SubagentStop" or "PostToolUse" or "PostToolUseFailure" or "PermissionDenied"
            => new(NodeStatus.Working, current.Status == NodeStatus.Working ? current.Detail : null),
        "PreToolUse" => new(NodeStatus.Working, e.ToolName),
        "PermissionRequest" => new(NodeStatus.Waiting, e.ToolName, AwaitsPermission: e.ToolName is not { } tool || !NotYesNoTools.Contains(tool)),
        // A permission prompt is also announced by PermissionRequest, which knows the tool; keep that detail
        "Notification" when e.NotificationType is "permission_prompt" or "elicitation_dialog"
            => current.Status == NodeStatus.Waiting ? current : new(NodeStatus.Waiting),
        "Stop" => new(NodeStatus.Done),
        "StopFailure" => new(NodeStatus.Error),
        "SessionStart" or "SessionEnd" => AgentStatus.Idle,
        _ => current,
    };

    /// <summary>Text for the node header, e.g. "Needs you · Bash".</summary>
    public static string? Describe(AgentStatus s) => s.Status switch
    {
        NodeStatus.Waiting => s.Detail is { } d ? $"Needs you · {d}" : "Needs you",
        NodeStatus.Working => s.Detail is { } d ? $"Working · {d}" : "Working",
        NodeStatus.Done => "Done",
        NodeStatus.Error => "Error",
        _ => null,
    };
}
