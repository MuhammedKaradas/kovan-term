using System.Windows;
using System.Windows.Input;
using KovanTerm.Services;
using KovanTerm.ViewModels;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

/// <summary>Tools: its own window, so the canvas's terminals don't cover it (#139).</summary>
public partial class ToolsWindow : FluentWindow
{
    private static ToolsWindow? _open;

    private ToolsWindow(ToolsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => Close()));
        Closed += (_, _) => _open = null;
    }

    /// <summary>Opens the window for <paramref name="project"/> (the current terminal's folder), or brings it forward.</summary>
    public static void Show(Window owner, string? project)
    {
        if (_open is { DataContext: ToolsViewModel open } && open.Project == project)
        {
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }
        _open?.Close();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var vm = new ToolsViewModel(p => ToolsInventory.Scan(home, p), project);
        _open = new ToolsWindow(vm) { Owner = owner };
        _open.Show();
        _ = vm.LoadAsync();
    }
}
