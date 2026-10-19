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
    /// <summary>Terminal dışındaki çerçevenin yüksekliği (başlık + renk çizgisi + boyutlandırma şeridi); yerleşim hesaplarında kullanılır.</summary>
    public const double ChromeHeight = 46;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ProfileId { get; init; }
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    public string? WorkingDirectory { get; init; }
    /// <summary>Başlık vurgu rengi (#RRGGBB); boşsa varsayılan.</summary>
    public string? Color { get; init; }
    /// <summary>Terminal alanının genişliği (tuval birimi).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial double Width { get; set; } = DefaultWidth;

    /// <summary>Terminal alanının yüksekliği (tuval birimi); başlık hariç.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial double Height { get; set; } = DefaultHeight;

    /// <summary>Terminal başlatılamayacaksa (ör. komut bulunamadı) kullanıcıya gösterilecek açıklama.</summary>
    public string? StartError { get; init; }
    public bool HasStartError => StartError != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bounds))]
    public partial Point Location { get; set; }

    /// <summary>Düğümün tuvaldeki dış sınırı (başlık dahil).</summary>
    public Rect Bounds => new(Location, new Size(Width, Height + ChromeHeight));

    /// <summary>Boyutu verilen miktar kadar değiştirir; en küçük/en büyük sınırlar uygulanır.</summary>
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
        // Eski/eksik kayıtta boyut 0 gelebilir
        Width = s.Width > 0 ? Math.Clamp(s.Width, MinWidth, MaxSize) : DefaultWidth,
        Height = s.Height > 0 ? Math.Clamp(s.Height, MinHeight, MaxSize) : DefaultHeight,
    };
}
