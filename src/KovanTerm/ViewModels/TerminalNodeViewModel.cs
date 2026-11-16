using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

public partial class TerminalNodeViewModel : ObservableObject
{
    public const double DefaultWidth = 560;
    public const double DefaultHeight = 340;
    public const double MinWidth = 240;
    public const double MinHeight = 120;
    public const double MaxSize = 4000;
    /// <summary>Height of the chrome around the terminal (header + color stripe + resize strip); used in layout calculations.</summary>
    public const double ChromeHeight = 46;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ProfileId { get; init; }
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    /// <summary>
    /// What the terminal actually runs: <see cref="CommandLine"/> plus per-run arguments such as the hook settings.
    /// Not persisted; the saved command stays reusable on the next run.
    /// </summary>
    public string? LaunchCommandLine { get; init; }
    public string StartupCommandLine => LaunchCommandLine ?? CommandLine;
    public string? WorkingDirectory { get; init; }
    /// <summary>Header accent color (#RRGGBB); default if empty.</summary>
    public string? Color { get; init; }
    /// <summary>Width of the terminal area (canvas units).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial double Width { get; set; } = DefaultWidth;

    /// <summary>Height of the terminal area (canvas units), excluding the header.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial double Height { get; set; } = DefaultHeight;

    /// <summary>Explanation shown to the user if the terminal can't be started (e.g. command not found).</summary>
    public string? StartError { get; init; }
    public bool HasStartError => StartError != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial Point Location { get; set; }

    /// <summary>Outer bounds of the node on the canvas (including the header).</summary>
    public Rect Bounds => new(Location, new Size(Width, Height + ChromeHeight));

    /// <summary>Changes the size by the given amount; min/max limits apply.</summary>
    public void ResizeBy(double dx, double dy)
    {
        Width = Math.Clamp(Width + dx, MinWidth, MaxSize);
        Height = Math.Clamp(Height + dy, MinHeight, MaxSize);
    }

    [ObservableProperty]
    public partial NodeStatus Status { get; set; }

    /// <summary>Short status text for the header, e.g. "Needs you · Bash"; <c>null</c> when there's nothing to say.</summary>
    [ObservableProperty]
    public partial string? StatusText { get; set; }

    /// <summary>The agent is asking a yes/no tool permission, which the header's Allow/Deny buttons can answer.</summary>
    [ObservableProperty]
    public partial bool AwaitsPermission { get; set; }

    /// <summary>
    /// The agent's conversation (Claude Code session id). Chosen when the node is created and resumed on the next
    /// launch; follows the agent's events, so a <c>/clear</c> moves the node to the new conversation.
    /// </summary>
    [ObservableProperty]
    public partial string? SessionId { get; set; }

    private AgentStatus _agent = AgentStatus.Idle;

    /// <summary>
    /// What Allow types into the terminal: the "Yes" row's key. Claude Code: <c>1</c>; Codex: <c>y</c>
    /// (learned from its status reports). Deny is Escape for both.
    /// </summary>
    public string AllowKey { get; private set; } = "1";

    /// <summary>Updates the status from the program's own OSC 7501 report.</summary>
    public void ApplyProgramStatus(ProgramStatusReport report)
    {
        if (report.App == "codex") AllowKey = "y";
        if (AgentStatusReducer.FromReport(report) is { } s) SetAgentStatus(s);
    }

    /// <summary>Updates the status from an agent event (see <see cref="AgentStatusReducer"/>).</summary>
    public void ApplyAgentEvent(AgentEvent e)
    {
        if (e.SessionId != null) SessionId = e.SessionId;
        SetAgentStatus(AgentStatusReducer.Apply(_agent, e));
    }

    /// <summary>
    /// The prompt was answered. Allowed: the agent carries on. Otherwise (or unknown, when the user answered in the
    /// terminal) it's idle: a refusal sends no event, and an approval is followed by PostToolUse, which sets Working.
    /// </summary>
    public void PromptAnswered(bool allowed)
    {
        if (Status == NodeStatus.Waiting)
            SetAgentStatus(allowed ? new AgentStatus(NodeStatus.Working) : AgentStatus.Idle);
    }

    private void SetAgentStatus(AgentStatus s)
    {
        _agent = s;
        Status = s.Status;
        StatusText = AgentStatusReducer.Describe(s);
        AwaitsPermission = s.AwaitsPermission;
    }

    public NodeState ToState() => new()
    {
        Id = Id,
        ProfileId = ProfileId,
        Title = Title,
        CommandLine = CommandLine,
        SessionId = SessionId,
        Color = Color,
        WorkingDirectory = WorkingDirectory,
        X = Location.X,
        Y = Location.Y,
        Width = Width,
        Height = Height,
    };

    public static TerminalNodeViewModel FromState(NodeState s, string? commandLine = null, string? startError = null, string? launchCommandLine = null) => new()
    {
        Id = s.Id,
        ProfileId = s.ProfileId,
        Title = s.Title,
        CommandLine = commandLine ?? s.CommandLine,
        LaunchCommandLine = launchCommandLine,
        SessionId = s.SessionId,
        Color = s.Color,
        WorkingDirectory = s.WorkingDirectory,
        StartError = startError,
        Location = new Point(s.X, s.Y),
        // Size may be 0 in old/incomplete saves
        Width = s.Width > 0 ? Math.Clamp(s.Width, MinWidth, MaxSize) : DefaultWidth,
        Height = s.Height > 0 ? Math.Clamp(s.Height, MinHeight, MaxSize) : DefaultHeight,
    };
}
