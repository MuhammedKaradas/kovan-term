using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace KovanTerm;

public class TerminalNode : INotifyPropertyChanged
{
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    public double Width { get; init; } = 560;
    public double Height { get; init; } = 340;

    private Point _location;
    public Point Location
    {
        get => _location;
        set { _location = value; OnPropertyChanged(); }
    }

    // Şimdilik sabit; sonraki adımda Claude hook'larından gelecek
    private Brush _statusBrush = Brushes.LimeGreen;
    public Brush StatusBrush
    {
        get => _statusBrush;
        set { _statusBrush = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
