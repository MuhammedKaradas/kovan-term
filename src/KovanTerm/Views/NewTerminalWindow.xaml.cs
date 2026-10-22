using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

public partial class NewTerminalWindow : Window
{
    private readonly NewTerminalViewModel _vm;

    public NewTerminalWindow(NewTerminalViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        // Close the window once the ViewModel sets the result
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewTerminalViewModel.DialogResult) && _vm.DialogResult != null)
            DialogResult = _vm.DialogResult;
    }

    private void ProfileItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.ConfirmCommand.CanExecute(null))
            _vm.ConfirmCommand.Execute(null);
    }
}
