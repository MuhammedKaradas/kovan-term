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
    public string? Color { get; init; }
    public string? WorkingDirectory { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}
