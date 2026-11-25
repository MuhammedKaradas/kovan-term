using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>
/// A Claude Code session running outside Kovan, as its hooks report it (<see cref="OutsideHook"/>): shown under
/// "Elsewhere" in the Agents panel. Kovan can't type into its terminal, so there's no Allow/Deny; it can open the
/// conversation on the canvas.
/// </summary>
public sealed partial class OutsideSessionViewModel(string sessionId) : ObservableObject
{
    private AgentStatus _agent = AgentStatus.Idle;

    public string SessionId { get; } = sessionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string? Folder { get; private set; }

    public string? TranscriptPath { get; private set; }

    public string Title => Folder is { Length: > 0 } f && Path.GetFileName(f.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : "Claude Code";

    [ObservableProperty]
    public partial NodeStatus Status { get; private set; } = NodeStatus.Idle;

    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    /// <summary>When it last reported: rows of sessions that went quiet for long (closed without SessionEnd) are dropped.</summary>
    public DateTime LastSeen { get; private set; } = DateTime.Now;

    /// <summary>Applies the event; did the session just start waiting for the user?</summary>
    public bool Apply(AgentEvent e, DateTime now)
    {
        LastSeen = now;
        if (e.Cwd is { Length: > 0 } cwd) Folder = cwd;
        if (e.TranscriptPath != null) TranscriptPath = e.TranscriptPath;
        var before = _agent.Status;
        _agent = AgentStatusReducer.Apply(_agent, e);
        Status = _agent.Status;
        StatusText = AgentStatusReducer.Describe(_agent);
        return Status == NodeStatus.Waiting && before != NodeStatus.Waiting;
    }
}
