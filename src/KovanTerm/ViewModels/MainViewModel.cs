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

    /// <summary>Görünür alanın tuval koordinatlarındaki boyutu; görünüm tarafından güncellenir.</summary>
    public Size ViewportSize { get; set; }

    [ObservableProperty]
    public partial long MemoryMb { get; set; }

    private readonly WorkspaceStore _store;
    private readonly IReadOnlyList<TerminalProfile> _profiles;
    private readonly CommandResolver _resolver;
    private readonly IDialogService _dialogs;
    private readonly List<string> _recentDirectories = [];
    private string? _lastProfileId;

    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    // Değişiklikten bu kadar sonra kaydedilir; sürükleme gibi art arda gelen değişiklikler tek yazıma iner
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public MainViewModel(WorkspaceStore store, ProfileStore profiles, CommandResolver resolver, IDialogService dialogs)
    {
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

    /// <summary>Profil ve çalışma dizini sorup yeni terminal açar. <paramref name="location"/> boşsa görünümün ortasına koyar.</summary>
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

    /// <summary>Bekleyen kaydı hemen yazar. Uygulama kapanırken çağrılır.</summary>
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
            // Kayıt başarısız olursa bir sonraki değişiklikte yeniden denenir
            Debug.WriteLine($"Çalışma alanı kaydedilemedi: {ex.Message}");
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
        // Üst üste binen terminaller birbirini örttüğü için en yakın boş yere yerleştir
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
    /// Profili bilinen düğümün komutu güncel profilden yeniden kurulur (CLI başka yere taşınmış olabilir).
    /// Çalıştırılabilir dosya bulunamazsa terminal başlatılmaz, düğümde hata gösterilir.
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
        $"'{executable}' bulunamadı.\nKurulu olduğundan ve PATH'e eklendiğinden emin ol, sonra bu düğümü kapatıp yeniden aç.";

    private void AddDefaultNodes()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var i = 0;
        foreach (var id in new[] { "powershell", "claude", "cmd" })
        {
            var profile = _profiles.First(p => p.Id == id);
            if (_resolver.Resolve(profile.Executable) is not { } path) continue;
            // 2 sütunlu ızgaraya diz
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
        // Durum (Status) kalıcı değil; yalnızca konum ve boyut değişince kaydet
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
