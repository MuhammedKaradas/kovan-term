using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxRecentDirectories = 10;

    public ObservableCollection<TerminalNodeViewModel> Nodes { get; } = new();

    [ObservableProperty]
    public partial double ViewportZoom { get; set; } = 1;

    [ObservableProperty]
    public partial Point ViewportLocation { get; set; }

    /// <summary>Size of the visible area in canvas coordinates; updated by the view.</summary>
    public Size ViewportSize { get; set; }

    [ObservableProperty]
    public partial long MemoryMb { get; set; }

    /// <summary>Light/dark theme preference; the view applies it (see <c>Views/ThemeService</c>).</summary>
    [ObservableProperty]
    public partial ThemePreference Theme { get; set; }

    private readonly WorkspaceStore _store;
    private readonly IReadOnlyList<TerminalProfile> _profiles;
    private readonly CommandResolver _resolver;
    private readonly IDialogService _dialogs;
    private readonly SettingsStore _settings;
    private readonly List<string> _recentDirectories = [];
    private string? _lastProfileId;

    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    // Saves this long after a change; bursts of changes such as dragging collapse into a single write
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private readonly ClaudeHooks? _hooks;
    private readonly ClaudeSessions _sessions;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    /// <summary>Agents currently working / waiting for the user (toolbar summary).</summary>
    [ObservableProperty]
    public partial int WorkingCount { get; set; }

    [ObservableProperty]
    public partial int WaitingCount { get; set; }

    /// <summary>The view pans to and focuses this node.</summary>
    public event Action<TerminalNodeViewModel>? GoToNodeRequested;

    /// <summary>An agent started waiting for the user; the view flashes the taskbar if the window isn't active.</summary>
    public event Action<TerminalNodeViewModel>? AttentionNeeded;

    /// <summary>Text to type into a node's terminal (e.g. the answer to a permission prompt).</summary>
    public event Action<TerminalNodeViewModel, string>? TerminalInputRequested;

    public MainViewModel(WorkspaceStore store, ProfileStore profiles, CommandResolver resolver, IDialogService dialogs, SettingsStore settings,
        AgentHookServer? hookServer = null, ClaudeSessions? sessions = null)
    {
        _sessions = sessions ?? new ClaudeSessions();
        if (hookServer != null)
        {
            _hooks = new ClaudeHooks(hookServer);
            hookServer.EventReceived += (id, e) => _dispatcher.BeginInvoke(() => OnAgentEvent(id, e));
        }
        _settings = settings;
        Theme = settings.Load().Theme;
        ShowAgentsPanel = settings.Load().ShowAgentsPanel;
        ShowChangesPanel = settings.Load().ShowChangesPanel;
        _changesTimer.Tick += (_, _) =>
        {
            _changesTimer.Stop();
            _ = Changes.RefreshAsync();
        };
        Changes.SendRequested += (node, message) => TerminalInputRequested?.Invoke(node, ChangesViewModel.BracketedPaste(message));
        _store = store;
        _profiles = profiles.Load();
        _resolver = resolver;
        _dialogs = dialogs;

        if (_store.Load() is { } state)
            Restore(state);
        else
            AddDefaultNodes();

        Nodes.CollectionChanged += OnNodesChanged;
        foreach (var node in Nodes)
            node.PropertyChanged += OnNodePropertyChanged;

        _saveTimer.Tick += (_, _) => SaveNow();
        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();
    }

    /// <summary>Asks for a profile and working directory, then opens a new terminal. Places it at the center of the viewport if <paramref name="location"/> is null.</summary>
    [RelayCommand]
    private void NewTerminal(Point? location)
    {
        var options = _profiles.Select(p => new ProfileOptionViewModel(p, _resolver.Resolve(p.Executable))).ToList();
        var dialog = new NewTerminalViewModel(_dialogs, options, _recentDirectories, _lastProfileId);
        if (!_dialogs.ShowNewTerminal(dialog) || dialog.SelectedProfile is not { IsAvailable: true } choice) return;

        var directory = Path.GetFullPath(dialog.WorkingDirectory);
        RememberDirectory(directory);
        _lastProfileId = choice.Profile.Id;
        AddNode(CreateNode(choice.Profile, choice.ResolvedPath!, directory, location ?? ViewportCenterForNewNode()));
    }

    [RelayCommand]
    private void CloseNode(TerminalNodeViewModel node)
    {
        Nodes.Remove(node);
        _hooks?.Remove(node.Id);
        _usage.Remove(node);
        if (CurrentNode == node) CurrentNode = null;
    }

    /// <summary>Goes to the next node waiting for the user, in reading order after the last one visited.</summary>
    [RelayCommand]
    private void GoToWaiting()
    {
        var waiting = NodeNavigation.ReadingOrder(Nodes.Where(n => n.Status == NodeStatus.Waiting), n => n.Bounds);
        if (waiting.Count == 0) return;
        var next = waiting.SkipWhile(n => n != _lastWaitingVisited).Skip(1).FirstOrDefault() ?? waiting[0];
        _lastWaitingVisited = next;
        GoToNodeRequested?.Invoke(next);
    }

    private TerminalNodeViewModel? _lastWaitingVisited;

    // Claude Code's permission prompt: "1" picks "Yes" wherever the highlight is (Enter would take the highlighted
    // row, which may be "allow for this session"); Escape is "No, and tell Claude what to do differently".
    [RelayCommand]
    private void AllowPermission(TerminalNodeViewModel node) => AnswerPermission(node, node.AllowKey, allowed: true);

    [RelayCommand]
    private void DenyPermission(TerminalNodeViewModel node) => AnswerPermission(node, "\x1b", allowed: false);

    private void AnswerPermission(TerminalNodeViewModel node, string keys, bool allowed)
    {
        if (!node.AwaitsPermission) return;
        TerminalInputRequested?.Invoke(node, keys);
        node.PromptAnswered(allowed);
        StatusLog($"{node.Title} answered allowed={allowed} -> {node.Status}");
        UpdateAgentCounts();
    }

    /// <summary>The user pressed a key that answers prompts (Enter, Escape, a digit) in the node's terminal.</summary>
    public void OnAnswerKeyTyped(TerminalNodeViewModel node)
    {
        node.PromptAnswered(allowed: false);
        UpdateAgentCounts();
    }

    private void OnAgentEvent(Guid nodeId, AgentEvent e)
    {
        if (Nodes.FirstOrDefault(n => n.Id == nodeId) is not { } node) return;
        StatusLog($"{node.Title} hook {e.Name} {e.ToolName}");
        UpdateStatus(node, () => node.ApplyAgentEvent(e));
        // A reply was just written to the transcript
        if (e.Name is "Stop" or "PostToolUse" or "PostToolUseFailure" or "SessionStart" && node.TranscriptPath != null)
            RefreshContext(node);
        // A tool may have changed files
        if (e.Name is "PostToolUse" or "Stop") ScheduleChangesRefresh(node);
    }

    // ---- Changes panel ----

    public ChangesViewModel Changes { get; } = new(new GitChanges());

    [ObservableProperty]
    public partial bool ShowChangesPanel { get; set; }

    partial void OnShowChangesPanelChanged(bool value)
    {
        _settings.Save(_settings.Load() with { ShowChangesPanel = value });
        if (value) _ = Changes.ShowAsync(CurrentNode);
    }

    [RelayCommand]
    private void ToggleChangesPanel() => ShowChangesPanel = !ShowChangesPanel;

    /// <summary>The node the user is working with (last clicked or navigated to); the Changes panel follows it.</summary>
    [ObservableProperty]
    public partial TerminalNodeViewModel? CurrentNode { get; set; }

    partial void OnCurrentNodeChanged(TerminalNodeViewModel? value)
    {
        if (ShowChangesPanel) _ = Changes.ShowAsync(value);
    }

    // Agents change files in bursts; refresh once things settle
    private readonly DispatcherTimer _changesTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private void ScheduleChangesRefresh(TerminalNodeViewModel node)
    {
        if (!ShowChangesPanel || node != CurrentNode) return;
        _changesTimer.Stop();
        _changesTimer.Start();
    }

    // ---- Agents panel ----

    [ObservableProperty]
    public partial bool ShowAgentsPanel { get; set; }

    partial void OnShowAgentsPanelChanged(bool value) => _settings.Save(_settings.Load() with { ShowAgentsPanel = value });

    [RelayCommand]
    private void ToggleAgentsPanel() => ShowAgentsPanel = !ShowAgentsPanel;

    [RelayCommand]
    private void GoToNode(TerminalNodeViewModel node) => GoToNodeRequested?.Invoke(node);

    private readonly Dictionary<TerminalNodeViewModel, TranscriptUsage> _usage = new();
    // Nodes with a read in flight, and those that got another event meanwhile (read once more afterwards)
    private readonly HashSet<TerminalNodeViewModel> _usageReading = new(), _usageAgain = new();

    /// <summary>Reads the node's new transcript lines off the UI thread and updates its context size.</summary>
    private void RefreshContext(TerminalNodeViewModel node)
    {
        if (!_usageReading.Add(node))
        {
            _usageAgain.Add(node);
            return;
        }
        if (!_usage.TryGetValue(node, out var usage)) _usage[node] = usage = new TranscriptUsage();
        var path = node.TranscriptPath!;
        Task.Run(() => usage.Update(path)).ContinueWith(t =>
        {
            _usageReading.Remove(node);
            if (t.Result && Nodes.Contains(node)) node.SetContextTokens(usage.ContextTokens);
            if (_usageAgain.Remove(node)) RefreshContext(node);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The program in the node's terminal reported its status (OSC 7501, e.g. Codex).</summary>
    public void OnProgramStatus(TerminalNodeViewModel node, IReadOnlyList<ProgramStatusReport> reports)
    {
        if (!Nodes.Contains(node)) return;
        foreach (var r in reports) StatusLog($"{node.Title} osc {r.State} {r.Kind} {r.Id}");
        UpdateStatus(node, () => { foreach (var r in reports) node.ApplyProgramStatus(r); });
        if (reports.Any(r => r.State is "done" or "idle")) ScheduleChangesRefresh(node);
    }

    // KOVAN_STATUS_LOG=<file>: every status event and the resulting status, for diagnosing agent integrations
    private static readonly string? StatusLogPath = Environment.GetEnvironmentVariable("KOVAN_STATUS_LOG");

    private static void StatusLog(string line)
    {
        if (StatusLogPath == null) return;
        try { File.AppendAllText(StatusLogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    private void UpdateStatus(TerminalNodeViewModel node, Action apply)
    {
        var wasWaiting = node.Status == NodeStatus.Waiting;
        apply();
        StatusLog($"{node.Title} -> {node.Status} {node.StatusText}");
        UpdateAgentCounts();
        if (!wasWaiting && node.Status == NodeStatus.Waiting) AttentionNeeded?.Invoke(node);
    }

    private void UpdateAgentCounts()
    {
        WorkingCount = Nodes.Count(n => n.Status == NodeStatus.Working);
        WaitingCount = Nodes.Count(n => n.Status == NodeStatus.Waiting);
    }

    /// <summary>Cycles the theme: system → dark → light → system.</summary>
    [RelayCommand]
    private void CycleTheme() => Theme = NextTheme(Theme);

    public static ThemePreference NextTheme(ThemePreference current) => current switch
    {
        ThemePreference.System => ThemePreference.Dark,
        ThemePreference.Dark => ThemePreference.Light,
        _ => ThemePreference.System,
    };

    partial void OnThemeChanged(ThemePreference value)
    {
        _settings.Save(_settings.Load() with { Theme = value });
    }

    /// <summary>Writes any pending save immediately. Called when the app is closing.</summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _store.Save(new WorkspaceState
            {
                Viewport = new ViewportState { Zoom = ViewportZoom, X = ViewportLocation.X, Y = ViewportLocation.Y },
                Nodes = Nodes.Select(n => n.ToState()).ToList(),
                RecentDirectories = _recentDirectories.ToList(),
                LastProfileId = _lastProfileId,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If saving fails, it's retried on the next change
            Debug.WriteLine($"Couldn't save workspace: {ex.Message}");
        }
    }

    private TerminalNodeViewModel CreateNode(TerminalProfile profile, string resolvedPath, string directory, Point location)
    {
        var id = Guid.NewGuid();
        var sessionId = ClaudeHooks.IsClaude(resolvedPath) ? Guid.NewGuid().ToString() : null;
        return new()
        {
            Id = id,
            ProfileId = profile.Id,
            Title = profile.Name,
            CommandLine = CommandResolver.BuildCommandLine(resolvedPath, profile.Arguments),
            LaunchCommandLine = LaunchCommandLine(id, resolvedPath, profile.Arguments, sessionId),
            SessionId = sessionId,
            Color = profile.Color,
            WorkingDirectory = directory,
            Location = location,
        };
    }

    /// <summary>
    /// For Claude Code: the command line with the node's conversation (<see cref="ClaudeSessions"/>) and this run's
    /// hook settings; otherwise <c>null</c> (run the saved command).
    /// </summary>
    private string? LaunchCommandLine(Guid nodeId, string resolvedPath, string arguments, string? sessionId)
    {
        if (!ClaudeHooks.IsClaude(resolvedPath)) return null;
        var extra = new List<string> { arguments };
        if (sessionId != null) extra.Add(_sessions.Arguments(sessionId, arguments));
        if (_hooks != null) extra.Add(_hooks.PrepareArguments(nodeId));
        var all = string.Join(' ', extra.Where(a => a.Length > 0));
        return all == arguments ? null : CommandResolver.BuildCommandLine(resolvedPath, all);
    }

    private void AddNode(TerminalNodeViewModel node)
    {
        // Overlapping terminals cover each other, so place it in the nearest free spot
        var viewport = ViewportSize.IsEmpty || ViewportSize.Width <= 0 ? (Rect?)null : new Rect(ViewportLocation, ViewportSize);
        node.Location = NodePlacement.FindFreeSpot(Nodes.Select(n => n.Bounds).ToList(), node.Location, node.Bounds.Size, viewport);
        Nodes.Add(node);
    }

    private Point ViewportCenterForNewNode()
    {
        var center = ViewportLocation + new Vector(ViewportSize.Width / 2, ViewportSize.Height / 2);
        return center - new Vector(TerminalNodeViewModel.DefaultWidth / 2, TerminalNodeViewModel.DefaultHeight / 2);
    }

    private void Restore(WorkspaceState state)
    {
        foreach (var s in state.Nodes)
            Nodes.Add(RestoreNode(s));
        ViewportZoom = state.Viewport.Zoom > 0 ? state.Viewport.Zoom : 1;
        ViewportLocation = new Point(state.Viewport.X, state.Viewport.Y);
        _recentDirectories.AddRange(state.RecentDirectories.Take(MaxRecentDirectories));
        _lastProfileId = state.LastProfileId;
    }

    /// <summary>
    /// For a node with a known profile, the command is rebuilt from the current profile (the CLI may have moved).
    /// If the executable can't be found, the terminal isn't started and the node shows an error.
    /// </summary>
    private TerminalNodeViewModel RestoreNode(NodeState s)
    {
        if (_profiles.FirstOrDefault(p => p.Id == s.ProfileId) is { } profile)
        {
            if (_resolver.Resolve(profile.Executable) is not { } path)
                return TerminalNodeViewModel.FromState(s, startError: NotFoundMessage(profile.Executable));
            // Nodes saved before sessions were tracked get a fresh conversation, as they did before
            var state = ClaudeHooks.IsClaude(path) && s.SessionId == null ? s with { SessionId = Guid.NewGuid().ToString() } : s;
            return TerminalNodeViewModel.FromState(state, CommandResolver.BuildCommandLine(path, profile.Arguments),
                launchCommandLine: LaunchCommandLine(s.Id, path, profile.Arguments, state.SessionId));
        }

        var exe = CommandResolver.FirstToken(s.CommandLine);
        return _resolver.Resolve(exe) != null
            ? TerminalNodeViewModel.FromState(s)
            : TerminalNodeViewModel.FromState(s, startError: NotFoundMessage(exe));
    }

    private static string NotFoundMessage(string executable) =>
        $"'{executable}' not found.\nMake sure it's installed and on PATH, then close this node and open it again.";

    private void AddDefaultNodes()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var i = 0;
        foreach (var id in new[] { "powershell", "claude", "cmd" })
        {
            var profile = _profiles.First(p => p.Id == id);
            if (_resolver.Resolve(profile.Executable) is not { } path) continue;
            // Lay out in a 2-column grid
            AddNode(CreateNode(profile, path, home, new Point(40 + (i % 2) * 600, 40 + (i / 2) * 400)));
            i++;
        }
    }

    private void RememberDirectory(string directory)
    {
        _recentDirectories.RemoveAll(d => string.Equals(d, directory, StringComparison.OrdinalIgnoreCase));
        _recentDirectories.Insert(0, directory);
        if (_recentDirectories.Count > MaxRecentDirectories)
            _recentDirectories.RemoveRange(MaxRecentDirectories, _recentDirectories.Count - MaxRecentDirectories);
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TerminalNodeViewModel n in e.NewItems) n.PropertyChanged += OnNodePropertyChanged;
        if (e.OldItems != null)
            foreach (TerminalNodeViewModel n in e.OldItems) n.PropertyChanged -= OnNodePropertyChanged;
        UpdateAgentCounts();
        ScheduleSave();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Status isn't persisted; save when location, size or the conversation (e.g. after /clear) changes
        if (e.PropertyName is nameof(TerminalNodeViewModel.Location) or nameof(TerminalNodeViewModel.Width) or nameof(TerminalNodeViewModel.Height)
            or nameof(TerminalNodeViewModel.SessionId))
            ScheduleSave();
    }

    partial void OnViewportZoomChanged(double value) => ScheduleSave();
    partial void OnViewportLocationChanged(Point value) => ScheduleSave();

    private void UpdateStats()
    {
        using var self = Process.GetCurrentProcess();
        MemoryMb = self.WorkingSet64 / 1024 / 1024;
    }
}
