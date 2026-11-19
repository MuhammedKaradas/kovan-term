using System.IO;
using System.Windows;
using System.Windows.Input;
using KovanTerm.Services;
using KovanTerm.Services.Stats;
using KovanTerm.ViewModels;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

/// <summary>Statistics: its own window, so the canvas's terminals don't cover it.</summary>
public partial class StatisticsWindow : FluentWindow
{
    private static StatisticsWindow? _open;

    private StatisticsWindow(StatisticsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => Close()));
        Closed += (_, _) => _open = null;
    }

    /// <summary>Opens the window, or brings it forward if it's open; reads the conversations in the background.</summary>
    public static void Show(Window owner)
    {
        if (_open != null)
        {
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }
        var cache = Path.Combine(AppPaths.DataDirectory, "stats-cache.json");
        var vm = new StatisticsViewModel(progress => WorkStats.Load(cache, progress: progress));
        _open = new StatisticsWindow(vm) { Owner = owner };
        _open.Show();
        _ = vm.LoadAsync();
    }
}
