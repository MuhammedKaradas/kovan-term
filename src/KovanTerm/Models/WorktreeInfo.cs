namespace KovanTerm.Models;

/// <summary>A git worktree Kovan created for a node, so its agent works on its own branch and folder.</summary>
/// <param name="RepoRoot">The main checkout the worktree was created from.</param>
/// <param name="Path">The worktree's folder.</param>
/// <param name="Branch">The branch checked out in it.</param>
/// <param name="BaseCommit">The commit the branch started from; the Changes panel diffs against it.</param>
public sealed record WorktreeInfo(string RepoRoot, string Path, string Branch, string BaseCommit);
