using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KovanTerm.ViewModels;

/// <summary>
/// One canvas the user switches between (a tab above the canvas), e.g. one per project. Its nodes keep running while
/// another workspace is shown; it remembers where it was looked at.
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>The tab's name is being edited.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>Agents on it waiting for the user / working (shown on the tab, so another workspace can call for attention).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText), nameof(HasWaiting))]
    public partial int WaitingCount { get; set; }

    public bool HasWaiting => WaitingCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    public partial int WorkingCount { get; set; }

    /// <summary>"2 waiting", "1 working" or <c>null</c>.</summary>
    public string? BadgeText => WaitingCount > 0 ? $"{WaitingCount} waiting" : WorkingCount > 0 ? $"{WorkingCount} working" : null;

    public double Zoom { get; set; } = 1;
    public Point Location { get; set; }

    /// <summary>The node last worked with here, made current again when switching back. Not persisted.</summary>
    public TerminalNodeViewModel? LastNode { get; set; }
}
