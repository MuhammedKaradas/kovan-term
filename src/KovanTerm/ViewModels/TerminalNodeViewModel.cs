using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Models;

namespace KovanTerm.ViewModels;

public partial class TerminalNodeViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    public double Width { get; init; } = 560;
    public double Height { get; init; } = 340;

    [ObservableProperty]
    public partial Point Location { get; set; }

    [ObservableProperty]
    public partial NodeStatus Status { get; set; }
}
