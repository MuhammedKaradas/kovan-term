using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using KovanTerm.ViewModels;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

public partial class SettingsWindow : FluentWindow
{
    private static SettingsWindow? _open;

    private SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => Close()));
        Closed += (_, _) => _open = null;
    }

    /// <summary>Opens the window, or brings it forward if it's open.</summary>
    public static void Show(Window owner, Func<SettingsViewModel> create)
    {
        if (_open != null)
        {
            _open.Activate();
            return;
        }
        _open = new SettingsWindow(create()) { Owner = owner };
        _open.Show();
    }

    // In the default editor for .json files, or Notepad if there's none
    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { FilePath: var path } || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }
}
