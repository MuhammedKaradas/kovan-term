namespace KovanTerm.Models;

/// <summary>Status of a terminal/agent node. Real values will come from agent hooks in v0.2.</summary>
public enum NodeStatus
{
    Idle,
    Working,
    Waiting,
    Done,
    Error,
}
