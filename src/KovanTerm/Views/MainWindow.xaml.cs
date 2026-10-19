using System.Windows;
using EasyWindowsTerminalControl;
using KovanTerm.ViewModels;

namespace KovanTerm.Views;

public partial class MainWindow : Window
{
    private readonly TerminalCanvasController _canvas;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        _canvas = new TerminalCanvasController(Editor);
    }

    // Terminal kontrolleri HwndHost olduğu için tuval kontrolcüsüne görünüm katmanında kaydedilir
    private void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is EasyTerminalControl term)
            _canvas.Register(term);
    }
}
