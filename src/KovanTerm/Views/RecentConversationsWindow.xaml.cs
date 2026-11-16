using System.ComponentModel;
using System.Windows.Input;
using KovanTerm.ViewModels;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

public partial class RecentConversationsWindow : FluentWindow
{
    private readonly RecentConversationsViewModel _vm;

    public RecentConversationsWindow(RecentConversationsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecentConversationsViewModel.DialogResult) && _vm.DialogResult != null)
            DialogResult = _vm.DialogResult;
    }

    private void Row_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.OpenCommand.CanExecute(null)) _vm.OpenCommand.Execute(null);
    }

    // Up/Down move through the list while typing in the search box
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up) || _vm.Rows.Count == 0) return;
        var i = _vm.Selected is { } s ? _vm.Rows.ToList().IndexOf(s) : -1;
        i = Math.Clamp(i + (e.Key == Key.Down ? 1 : -1), 0, _vm.Rows.Count - 1);
        _vm.Selected = _vm.Rows[i];
        List.ScrollIntoView(_vm.Selected);
        e.Handled = true;
    }
}
