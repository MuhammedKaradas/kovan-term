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
        // Subagent events don't change the node: background subagents start and stop after the main agent's Stop,
        // and a subagent the agent waits for is already covered by the agent's own tool call (Working).
        "SubagentStart" or "SubagentStop" => current,
        "UserPromptSubmit" or "PostToolUse" or "PostToolUseFailure" or "PermissionDenied"
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

    /// <summary>
    /// Status from a program's own OSC 7501 report (<see cref="ProgramStatusParser"/>); <c>null</c> for reports
    /// about sub-records such as subagents, which don't change the node.
    /// </summary>
    public static AgentStatus? FromReport(ProgramStatusReport r)
    {
        if (r.Id != null) return null;
        return r.State switch
        {
            "working" => new(NodeStatus.Working, r.Message),
            "blocked" => new(NodeStatus.Waiting, r.Message, AwaitsPermission: r.Kind == "permission"),
            "done" => new(NodeStatus.Done),
            "error" => new(NodeStatus.Error, r.Message),
            _ => AgentStatus.Idle, // idle, clear
        };
    }

    /// <summary>Text for the node header, e.g. "Needs you · Bash".</summary>
    public static string? Describe(AgentStatus s) => s.Status switch
    {
        NodeStatus.Waiting => s.Detail is { } d ? $"Needs you · {d}" : "Needs you",
        NodeStatus.Working => s.Detail is { } d ? $"Working · {d}" : "Working",
        NodeStatus.Done => "Done",
        NodeStatus.Error => s.Detail is { } d ? $"Error · {d}" : "Error",
        _ => null,
    };
}
