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
    public ObservableCollection<TerminalNodeViewModel> Nodes { get; } = new();

    public IReadOnlyList<TerminalLaunch> QuickLaunches { get; } =
    [
        new("PowerShell", "powershell.exe -NoLogo"),
        new("Claude Code", "cmd.exe /k claude"),
        new("cmd", "cmd.exe"),
    ];

    [ObservableProperty]
    public partial double ViewportZoom { get; set; } = 1;

    [ObservableProperty]
    public partial Point ViewportLocation { get; set; }

    [ObservableProperty]
    public partial long MemoryMb { get; set; }

    private readonly WorkspaceStore _store;
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    // Değişiklikten bu kadar sonra kaydedilir; sürükleme gibi art arda gelen değişiklikler tek yazıma iner
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _spawnCount;

    public MainViewModel(WorkspaceStore store)
    {
        _store = store;

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

    [RelayCommand]
    private void AddTerminal(TerminalLaunch launch)
    {
        // 2 sütunlu ızgaraya diz
        var i = _spawnCount++;
        Nodes.Add(new TerminalNodeViewModel
        {
            Title = launch.Title,
            CommandLine = launch.CommandLine,
            Location = new Point(40 + (i % 2) * 600, 40 + (i / 2) * 400),
        });
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
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kayıt başarısız olursa bir sonraki değişiklikte yeniden denenir
            Debug.WriteLine($"Çalışma alanı kaydedilemedi: {ex.Message}");
        }
    }

    private void Restore(WorkspaceState state)
    {
        foreach (var node in state.Nodes)
            Nodes.Add(TerminalNodeViewModel.FromState(node));
        ViewportZoom = state.Viewport.Zoom > 0 ? state.Viewport.Zoom : 1;
        ViewportLocation = new Point(state.Viewport.X, state.Viewport.Y);
        _spawnCount = Nodes.Count;
    }

    private void AddDefaultNodes()
    {
        AddTerminal(QuickLaunches[0]);
        AddTerminal(QuickLaunches[1]);
        AddTerminal(QuickLaunches[2]);
        AddTerminal(QuickLaunches[0] with { Title = "PowerShell 2" });
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
        // Durum (Status) kalıcı değil; yalnızca konum değişince kaydet
        if (e.PropertyName == nameof(TerminalNodeViewModel.Location))
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
