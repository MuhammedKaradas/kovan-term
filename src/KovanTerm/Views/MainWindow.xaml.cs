using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using EasyWindowsTerminalControl;

namespace KovanTerm.Views;

public partial class MainWindow : Window
{
    public ObservableCollection<TerminalNode> Nodes { get; } = new();

    private readonly TerminalCanvasController _canvas;
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _spawnCount;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _canvas = new TerminalCanvasController(Editor);

        AddNode("PowerShell", "powershell.exe -NoLogo");
        AddNode("Claude Code", "cmd.exe /k claude");
        AddNode("cmd", "cmd.exe");
        AddNode("PowerShell 2", "powershell.exe -NoLogo");

        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();
    }

    private void AddNode(string title, string commandLine)
    {
        // 2 sütunlu ızgaraya diz
        var i = _spawnCount++;
        Nodes.Add(new TerminalNode
        {
            Title = title,
            CommandLine = commandLine,
            Location = new Point(40 + (i % 2) * 600, 40 + (i / 2) * 400),
        });
    }

    private void AddPowerShell_Click(object sender, RoutedEventArgs e) => AddNode("PowerShell", "powershell.exe -NoLogo");
    private void AddClaude_Click(object sender, RoutedEventArgs e) => AddNode("Claude Code", "cmd.exe /k claude");
    private void AddCmd_Click(object sender, RoutedEventArgs e) => AddNode("cmd", "cmd.exe");

    private void CloseNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TerminalNode node })
            Nodes.Remove(node);
    }

    private void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is EasyTerminalControl term)
            _canvas.Register(term);
    }

    private void UpdateStats()
    {
        using var self = Process.GetCurrentProcess();
        var mb = self.WorkingSet64 / 1024 / 1024;
        StatsText.Text = $"Uygulama RAM: {mb} MB   |   Terminal: {Nodes.Count}   |   Zoom: {Editor.ViewportZoom:0.00}";
    }
}
