using System.IO;
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
    /// <summary>For WSL nodes a Linux path (or <c>~</c>), passed via <c>wsl.exe --cd</c>.</summary>
    public string? WorkingDirectory { get; init; }
    public bool IsWsl { get; init; }
    /// <summary>Directory the process starts in on Windows; WSL nodes start in the user folder and change directory themselves.</summary>
    public string? ProcessDirectory => IsWsl ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : WorkingDirectory;

    /// <summary>
    /// A WSL node waits for its distro to boot before the terminal starts (see <see cref="Services.WslLauncher.StartDistroAsync"/>);
    /// the node shows <see cref="StartingText"/> meanwhile.
    /// </summary>
    [ObservableProperty]
    public partial bool IsStarting { get; set; }

    public string? StartingText { get; set; }
    /// <summary>The git worktree this node's agent works in (created by Kovan); <c>null</c> for a normal folder.</summary>
    public WorktreeInfo? Worktree { get; init; }
    public string? BranchText => Worktree?.Branch;
    public string MergeLabel => Worktree?.BaseBranch is { } b ? $"Merge into {b}" : "Merge";
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

    // ---- Web preview ----

    /// <summary>A web preview node (WebView2) instead of a terminal.</summary>
    public bool IsBrowser { get; init; }

    /// <summary>The preview's address, edited in its address bar.</summary>
    [ObservableProperty]
    public partial string? Url { get; set; }

    /// <summary>
    /// A terminal: the last local web address it printed (a dev server's <c>http://localhost:3000</c>), offered as
    /// "Preview" in its header; <c>null</c> until one shows up.
    /// </summary>
    [ObservableProperty]
    public partial string? PreviewUrl { get; set; }

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

    /// <summary>Which agent runs in the node (<c>claude</c>, <c>codex</c>, <c>gemini</c>), from its command line; <c>null</c> otherwise.</summary>
    public string? AgentKind => _agentKind ??= ScreenRules.AgentOf(CommandLine) ?? "";
    private string? _agentKind;

    /// <summary>
    /// What Allow types into the terminal: the "Yes" row's key. Claude Code: <c>1</c>; Codex: <c>y</c>;
    /// Gemini CLI: Enter (its first row, "Allow once", is highlighted). Deny is Escape for all.
    /// </summary>
    public string AllowKey => _allowKey ?? AgentKind switch { "codex" => "y", "gemini" => "\r", _ => "1" };
    private string? _allowKey;

    /// <summary>Updates the status from the program's own OSC 7501 report.</summary>
    public void ApplyProgramStatus(ProgramStatusReport report)
    {
        _reports = true;
        if (report.App == "codex") _allowKey = "y";
        if (AgentStatusReducer.FromReport(report) is { } s) SetAgentStatus(s);
    }

    /// <summary>Updates the status from an agent event (see <see cref="AgentStatusReducer"/>).</summary>
    public void ApplyAgentEvent(AgentEvent e)
    {
        _reports = true;
        if (e.SessionId != null) SessionId = e.SessionId;
        if (e.TranscriptPath != null) TranscriptPath = e.TranscriptPath;
        if (e.AgentId != null)
        {
            if (e.Name == "SubagentStart") _subagents[e.AgentId] = e.AgentType is { Length: > 0 } t ? t : "agent";
            else if (e.Name == "SubagentStop") _subagents.Remove(e.AgentId);
            SubagentsText = DescribeSubagents(_subagents.Values);
        }
        // A new conversation starts without subagents (e.g. after /clear)
        if (e.Name == "SessionStart" && _subagents.Count > 0)
        {
            _subagents.Clear();
            SubagentsText = null;
        }
        SetAgentStatus(AgentStatusReducer.Apply(_agent, e));
    }

    // ---- Agents panel ----

    /// <summary>The conversation's transcript file, from the agent's events.</summary>
    public string? TranscriptPath { get; private set; }

    /// <summary>Context the agent's conversation uses, e.g. "ctx 65k"; <c>null</c> until known.</summary>
    [ObservableProperty]
    public partial string? ContextText { get; private set; }

    public void SetContextTokens(long? tokens) => ContextText = tokens is { } t ? $"ctx {TranscriptUsage.Format(t)}" : null;

    private readonly Dictionary<string, string> _subagents = new();

    /// <summary>Running subagents, e.g. "2 subagents: Explore, Plan"; <c>null</c> when none.</summary>
    [ObservableProperty]
    public partial string? SubagentsText { get; private set; }

    public static string? DescribeSubagents(IEnumerable<string> types)
    {
        var list = types.ToList();
        if (list.Count == 0) return null;
        var names = string.Join(", ", list.GroupBy(t => t).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));
        return list.Count == 1 ? $"1 subagent: {names}" : $"{list.Count} subagents: {names}";
    }

    /// <summary>
    /// Panel grouping: the working folder's name; for a worktree, its repository's, so all agents on one repository
    /// stay together.
    /// </summary>
    public string GroupName => (Worktree?.RepoRoot ?? WorkingDirectory) is { Length: > 0 } dir
        ? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))) is { Length: > 0 } name ? name : dir
        : "(no folder)";

    /// <summary>Panel order within a group: waiting first, then working, then the rest.</summary>
    public int StatusOrder => Status switch
    {
        NodeStatus.Waiting => 0,
        NodeStatus.Working => 1,
        NodeStatus.Error => 2,
        _ => 3,
    };

    partial void OnStatusChanged(NodeStatus value) => OnPropertyChanged(nameof(StatusOrder));

    /// <summary>
    /// The prompt was answered. Allowed: the agent carries on. Otherwise (or unknown, when the user answered in the
    /// terminal) it's idle: a refusal sends no event, and an approval is followed by PostToolUse, which sets Working.
    /// </summary>
    public void PromptAnswered(bool allowed)
    {
        if (Status != NodeStatus.Waiting) return;
        // The prompt is gone; the next screen read says what's there now
        if (_screen?.Status == NodeStatus.Waiting) _screen = null;
        SetAgentStatus(allowed ? new AgentStatus(NodeStatus.Working) : AgentStatus.Idle);
    }

    // ---- Where the status comes from ----

    /// <summary>Has the agent reported anything itself (hooks, OSC 7501)? Until then the screen decides.</summary>
    private bool _reports;
    /// <summary>The latest screen rule result (see <see cref="ScreenRules"/>); <c>null</c> = no opinion.</summary>
    private AgentStatus? _screen;

    /// <summary>The bottom of the terminal screen was read and matched against the screen rules.</summary>
    public void ApplyScreen(AgentStatus? screen)
    {
        // An agent with no other source going from working to idle has finished: show it as done
        if (!_reports && screen == AgentStatus.Idle && _screen?.Status == NodeStatus.Working) screen = new AgentStatus(NodeStatus.Done);
        else if (!_reports && screen == AgentStatus.Idle && _screen?.Status == NodeStatus.Done) screen = _screen;
        if (screen == _screen) return;
        _screen = screen;
        Show();
    }

    private void SetAgentStatus(AgentStatus s)
    {
        _agent = s;
        Show();
    }

    private void Show()
    {
        var s = Combine(_agent, _screen, _reports);
        Status = s.Status;
        StatusText = AgentStatusReducer.Describe(s);
        AwaitsPermission = s.AwaitsPermission;
    }

    /// <summary>
    /// What the node shows. The agent's own reports win, except that a prompt seen on screen which they didn't report
    /// (a folder trust question, say) still shows as waiting; an agent that reports nothing is shown from its screen.
    /// </summary>
    public static AgentStatus Combine(AgentStatus reported, AgentStatus? screen, bool agentReports)
    {
        if (screen is { Status: NodeStatus.Waiting } && reported.Status != NodeStatus.Waiting) return screen;
        return agentReports ? reported : screen ?? reported;
    }

    public NodeState ToState() => new()
    {
        Id = Id,
        ProfileId = ProfileId,
        Title = Title,
        CommandLine = CommandLine,
        SessionId = SessionId,
        Worktree = Worktree,
        Url = IsBrowser ? Url : null,
        Color = Color,
        WorkingDirectory = WorkingDirectory,
        Wsl = IsWsl,
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
        Worktree = s.Worktree,
        IsBrowser = s.Url != null,
        Url = s.Url,
        Color = s.Color,
        WorkingDirectory = s.WorkingDirectory,
        IsWsl = s.Wsl,
        StartError = startError,
        Location = new Point(s.X, s.Y),
        // Size may be 0 in old/incomplete saves
        Width = s.Width > 0 ? Math.Clamp(s.Width, MinWidth, MaxSize) : DefaultWidth,
        Height = s.Height > 0 ? Math.Clamp(s.Height, MinHeight, MaxSize) : DefaultHeight,
    };
}
