using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Models;

namespace KovanTerm.ViewModels;

public partial class TerminalNodeViewModel : ObservableObject
{
    public const double DefaultWidth = 560;
    public const double DefaultHeight = 340;
    public const double MinWidth = 240;
    public const double MinHeight = 120;
    public const double MaxSize = 4000;
    /// <summary>Height of the chrome around the terminal (header + color stripe + resize strip); used in layout calculations.</summary>
    public const double ChromeHeight = 46;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ProfileId { get; init; }
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    public string? WorkingDirectory { get; init; }
    /// <summary>Header accent color (#RRGGBB); default if empty.</summary>
    public string? Color { get; init; }
    /// <summary>Width of the terminal area (canvas units).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial double Width { get; set; } = DefaultWidth;

    /// <summary>Height of the terminal area (canvas units), excluding the header.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial double Height { get; set; } = DefaultHeight;

    /// <summary>Explanation shown to the user if the terminal can't be started (e.g. command not found).</summary>
    public string? StartError { get; init; }
    public bool HasStartError => StartError != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial Point Location { get; set; }

    /// <summary>Outer bounds of the node on the canvas (including the header).</summary>
    public Rect Bounds => new(Location, new Size(Width, Height + ChromeHeight));

    /// <summary>Changes the size by the given amount; min/max limits apply.</summary>
    public void ResizeBy(double dx, double dy)
    {
        Width = Math.Clamp(Width + dx, MinWidth, MaxSize);
        Height = Math.Clamp(Height + dy, MinHeight, MaxSize);
    }

    [ObservableProperty]
    public partial NodeStatus Status { get; set; }

    public NodeState ToState() => new()
    {
        Id = Id,
        ProfileId = ProfileId,
        Title = Title,
        CommandLine = CommandLine,
        Color = Color,
        WorkingDirectory = WorkingDirectory,
        X = Location.X,
        Y = Location.Y,
        Width = Width,
        Height = Height,
    };

    public static TerminalNodeViewModel FromState(NodeState s, string? commandLine = null, string? startError = null) => new()
    {
        Id = s.Id,
        ProfileId = s.ProfileId,
        Title = s.Title,
        CommandLine = commandLine ?? s.CommandLine,
        Color = s.Color,
        WorkingDirectory = s.WorkingDirectory,
        StartError = startError,
        Location = new Point(s.X, s.Y),
        // Size may be 0 in old/incomplete saves
        Width = s.Width > 0 ? Math.Clamp(s.Width, MinWidth, MaxSize) : DefaultWidth,
        Height = s.Height > 0 ? Math.Clamp(s.Height, MinHeight, MaxSize) : DefaultHeight,
    };
}
