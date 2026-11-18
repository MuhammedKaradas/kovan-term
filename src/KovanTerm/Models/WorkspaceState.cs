namespace KovanTerm.Models;

/// <summary>Workspace persisted to disk. Stay backward compatible when adding fields; bump <see cref="CurrentVersion"/> on breaking changes.</summary>
public sealed record WorkspaceState
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public ViewportState Viewport { get; init; } = new();
    public List<NodeState> Nodes { get; init; } = [];
    /// <summary>Most recently used working directories, newest first.</summary>
    public List<string> RecentDirectories { get; init; } = [];
    public string? LastProfileId { get; init; }
    /// <summary>The agents' shared task board and recent messages.</summary>
    public Services.Mesh.MeshBoardState? Board { get; init; }
}

public sealed record ViewportState
{
    public double Zoom { get; init; } = 1;
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed record NodeState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ProfileId { get; init; }
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    /// <summary>The agent's conversation id (Claude Code), resumed on the next launch.</summary>
    public string? SessionId { get; init; }
    /// <summary>The git worktree Kovan created for this node, if any.</summary>
    public WorktreeInfo? Worktree { get; init; }
    /// <summary>A web preview node's address; <c>null</c> for a terminal.</summary>
    public string? Url { get; init; }
    public string? Color { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool Wsl { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}
