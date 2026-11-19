namespace KovanTerm.Models;

/// <summary>Workspace persisted to disk. Stay backward compatible when adding fields; bump <see cref="CurrentVersion"/> on breaking changes.</summary>
public sealed record WorkspaceState
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    /// <summary>The active workspace's viewport (kept for older versions, which read only this).</summary>
    public ViewportState Viewport { get; init; } = new();
    /// <summary>The canvases the user switches between; empty in files from before workspaces (one implicit workspace).</summary>
    public List<WorkspaceInfo> Workspaces { get; init; } = [];
    public Guid? ActiveWorkspaceId { get; init; }
    public List<NodeState> Nodes { get; init; } = [];
    /// <summary>Most recently used working directories, newest first.</summary>
    public List<string> RecentDirectories { get; init; } = [];
    public string? LastProfileId { get; init; }
    /// <summary>The agents' shared task board and recent messages.</summary>
    public Services.Mesh.MeshBoardState? Board { get; init; }
}

/// <summary>One canvas: its name and where it was looked at last. Its nodes point to it with <see cref="NodeState.WorkspaceId"/>.</summary>
public sealed record WorkspaceInfo
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public ViewportState Viewport { get; init; } = new();
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
    /// <summary>The workspace the node is on; <c>null</c> (older files) or unknown means the first one.</summary>
    public Guid? WorkspaceId { get; init; }
    public string? ProfileId { get; init; }
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    /// <summary>The agent's conversation id (Claude Code), resumed on the next launch.</summary>
    public string? SessionId { get; init; }
    /// <summary>The git worktree Kovan created for this node, if any.</summary>
    public WorktreeInfo? Worktree { get; init; }
    /// <summary>A web preview node's address; <c>null</c> for a terminal.</summary>
    public string? Url { get; init; }
    /// <summary>A note node's Markdown text; <c>null</c> for other nodes.</summary>
    public string? Note { get; init; }
    /// <summary>A group frame (its title is the group's name); dragging it moves the nodes inside.</summary>
    public bool Group { get; init; }
    public string? Color { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool Wsl { get; init; }
    /// <summary>Runs in tmux session <c>kovan-&lt;id&gt;</c> inside WSL, which outlives the terminal.</summary>
    public bool Persistent { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}
