namespace KovanTerm.Models;

/// <summary>A git worktree Kovan created for a node, so its agent works on its own branch and folder.</summary>
/// <param name="RepoRoot">The main checkout the worktree was created from.</param>
/// <param name="Path">The worktree's folder.</param>
/// <param name="Branch">The branch checked out in it.</param>
/// <param name="BaseCommit">The commit the branch started from; the Changes panel diffs against it.</param>
/// <param name="BaseBranch">The main checkout's branch at that time, where the work is merged back; <c>null</c> if it
/// was a detached HEAD (or the worktree was saved by an older version).</param>
public sealed record WorktreeInfo(string RepoRoot, string Path, string Branch, string BaseCommit, string? BaseBranch = null);
