using System.ComponentModel;
using KovanTerm.ViewModels;
using Wpf.Ui.Controls;

namespace KovanTerm.Views;

public partial class BroadcastWindow : FluentWindow
{
    private readonly BroadcastViewModel _vm;

    public BroadcastWindow(BroadcastViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BroadcastViewModel.DialogResult) && _vm.DialogResult != null)
            DialogResult = _vm.DialogResult;
    }
}
