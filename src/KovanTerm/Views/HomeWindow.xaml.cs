using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KovanTerm.Services;
using KovanTerm.Services.Stats;
using KovanTerm.ViewModels;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

/// <summary>Home: its own window, so the canvas's terminals don't cover it. Its lists follow the agents while it's open.</summary>
public partial class HomeWindow : FluentWindow
{
    private static HomeWindow? _open;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };

    private HomeWindow(HomeViewModel vm, Action<TerminalNodeViewModel> goTo)
    {
        InitializeComponent();
        DataContext = vm;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => Close()));
        vm.GoToRequested += goTo;
        // Cheap: the lists are only touched when what they show changed
        _refresh.Tick += (_, _) => vm.Refresh();
        _refresh.Start();
        Closed += (_, _) =>
        {
            _refresh.Stop();
            vm.GoToRequested -= goTo;
            _open = null;
        };
    }

    /// <summary>Opens Home, or brings it forward; <paramref name="goTo"/> shows a node in the main window.</summary>
    public static void Show(Window owner, MainViewModel main, Action<TerminalNodeViewModel> goTo)
    {
        if (_open != null)
        {
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }
        var cache = Path.Combine(AppPaths.DataDirectory, "stats-cache.json");
        var vm = new HomeViewModel(main, () => WorkStats.Load(cache));
        _open = new HomeWindow(vm, goTo) { Owner = owner };
        _open.Show();
        _ = vm.LoadStatsAsync();
    }

    private void Agent_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TerminalNodeViewModel node) (DataContext as HomeViewModel)?.GoToCommand.Execute(node);
    }

    private void PullRequest_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PullRequestRow row) (DataContext as HomeViewModel)?.GoToPullRequestCommand.Execute(row);
    }

    private void Elsewhere_Click(object sender, MouseButtonEventArgs e) => (DataContext as HomeViewModel)?.ShowElsewhereCommand.Execute(null);
}
