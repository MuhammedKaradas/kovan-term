using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Models;

namespace KovanTerm.ViewModels;

public partial class TerminalNodeViewModel : ObservableObject
{
    public const double DefaultWidth = 560;
    public const double DefaultHeight = 340;
    /// <summary>Başlık + profil rengi çizgisinin yaklaşık yüksekliği; yerleşim hesaplarında kullanılır.</summary>
    public const double HeaderHeight = 34;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ProfileId { get; init; }
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    public string? WorkingDirectory { get; init; }
    /// <summary>Başlık vurgu rengi (#RRGGBB); boşsa varsayılan.</summary>
    public string? Color { get; init; }
    public double Width { get; init; } = DefaultWidth;
    public double Height { get; init; } = DefaultHeight;

    /// <summary>Terminal başlatılamayacaksa (ör. komut bulunamadı) kullanıcıya gösterilecek açıklama.</summary>
    public string? StartError { get; init; }
    public bool HasStartError => StartError != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial Point Location { get; set; }

    /// <summary>Düğümün tuvaldeki dış sınırı (başlık dahil).</summary>
    public Rect Bounds => new(Location, new Size(Width, Height + HeaderHeight));

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
        // Eski/eksik kayıtta boyut 0 gelebilir
        Width = s.Width > 0 ? s.Width : DefaultWidth,
        Height = s.Height > 0 ? s.Height : DefaultHeight,
    };
}
