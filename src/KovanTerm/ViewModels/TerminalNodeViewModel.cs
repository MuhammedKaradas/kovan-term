using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Models;

namespace KovanTerm.ViewModels;

public partial class TerminalNodeViewModel : ObservableObject
{
    public const double DefaultWidth = 560;
    public const double DefaultHeight = 340;

    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    public string? WorkingDirectory { get; init; }
    public double Width { get; init; } = DefaultWidth;
    public double Height { get; init; } = DefaultHeight;

    [ObservableProperty]
    public partial Point Location { get; set; }

    [ObservableProperty]
    public partial NodeStatus Status { get; set; }

    public NodeState ToState() => new()
    {
        Id = Id,
        Title = Title,
        CommandLine = CommandLine,
        WorkingDirectory = WorkingDirectory,
        X = Location.X,
        Y = Location.Y,
        Width = Width,
        Height = Height,
    };

    public static TerminalNodeViewModel FromState(NodeState s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        CommandLine = s.CommandLine,
        WorkingDirectory = s.WorkingDirectory,
        Location = new Point(s.X, s.Y),
        // Eski/eksik kayıtta boyut 0 gelebilir
        Width = s.Width > 0 ? s.Width : DefaultWidth,
        Height = s.Height > 0 ? s.Height : DefaultHeight,
    };
}
