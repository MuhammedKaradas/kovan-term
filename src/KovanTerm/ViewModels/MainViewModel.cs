using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Models;

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

    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _spawnCount;

    public MainViewModel()
    {
        AddTerminal(QuickLaunches[0]);
        AddTerminal(QuickLaunches[1]);
        AddTerminal(QuickLaunches[2]);
        AddTerminal(QuickLaunches[0] with { Title = "PowerShell 2" });

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

    private void UpdateStats()
    {
        using var self = Process.GetCurrentProcess();
        MemoryMb = self.WorkingSet64 / 1024 / 1024;
    }
}
