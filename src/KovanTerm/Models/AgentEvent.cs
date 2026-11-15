namespace KovanTerm.Models;

/// <summary>A lifecycle event reported by an agent running in a node (for now: a Claude Code hook).</summary>
/// <param name="Name">Event name, e.g. <c>PreToolUse</c>, <c>PermissionRequest</c>, <c>Stop</c>.</param>
/// <param name="SessionId">The agent's conversation id, if it sent one.</param>
/// <param name="ToolName">The tool the event is about (tool and permission events).</param>
/// <param name="NotificationType">For <c>Notification</c>: <c>permission_prompt</c>, <c>idle_prompt</c>…</param>
public sealed record AgentEvent(string Name, string? SessionId = null, string? ToolName = null, string? NotificationType = null);
