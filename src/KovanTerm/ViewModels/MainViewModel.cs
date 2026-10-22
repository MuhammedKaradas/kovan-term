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

    public MainViewModel(WorkspaceStore store, ProfileStore profiles, CommandResolver resolver, IDialogService dialogs, SettingsStore settings)
    {
        _settings = settings;
        Theme = settings.Load().Theme;
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
    private void CloseNode(TerminalNodeViewModel node) => Nodes.Remove(node);

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

    private static TerminalNodeViewModel CreateNode(TerminalProfile profile, string resolvedPath, string directory, Point location) => new()
    {
        ProfileId = profile.Id,
        Title = profile.Name,
        CommandLine = CommandResolver.BuildCommandLine(resolvedPath, profile.Arguments),
        Color = profile.Color,
        WorkingDirectory = directory,
        Location = location,
    };

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
            return _resolver.Resolve(profile.Executable) is { } path
                ? TerminalNodeViewModel.FromState(s, CommandResolver.BuildCommandLine(path, profile.Arguments))
                : TerminalNodeViewModel.FromState(s, startError: NotFoundMessage(profile.Executable));
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
        ScheduleSave();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Status isn't persisted; save only when location or size changes
        if (e.PropertyName is nameof(TerminalNodeViewModel.Location) or nameof(TerminalNodeViewModel.Width) or nameof(TerminalNodeViewModel.Height))
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
