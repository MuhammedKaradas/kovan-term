using System.Windows;

namespace KovanTerm.ViewModels;

/// <summary>
/// Yeni düğüm için boş yer bulur. Terminaller HWND olduğu için üst üste binen düğümler birbirinin
/// başlığını örter (airspace); bu yüzden yeni düğüm hiçbir düğümle kesişmeyecek şekilde yerleştirilir.
/// </summary>
public static class NodePlacement
{
    public const double Gap = 40;

    /// <summary>
    /// <paramref name="desired"/> boşsa onu, değilse en yakın boş konumu döndürür.
    /// Adaylar: istenen noktanın etrafındaki ızgara ve mevcut düğümlerin sağı/altı/solu/üstü.
    /// <paramref name="viewport"/> verilirse tamamen görünür adaylar tercih edilir.
    /// </summary>
    public static Point FindFreeSpot(IReadOnlyCollection<Rect> occupied, Point desired, Size size, Rect? viewport = null, int maxRings = 6)
    {
        if (IsFree(occupied, desired, size) && IsVisible(viewport, desired, size)) return desired;

        Point? best = null;
        var bestScore = double.MaxValue;
        foreach (var candidate in Candidates(occupied, desired, size, maxRings))
        {
            if (!IsFree(occupied, candidate, size)) continue;
            var score = (candidate - desired).LengthSquared;
            if (!IsVisible(viewport, candidate, size)) score += 1e12; // görünmeyen yer ancak başka çare yoksa
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best ?? desired;
    }

    private static IEnumerable<Point> Candidates(IEnumerable<Rect> occupied, Point desired, Size size, int maxRings)
    {
        foreach (var o in occupied)
        {
            yield return new Point(o.Right + Gap, o.Top);                 // sağı
            yield return new Point(o.Left, o.Bottom + Gap);               // altı
            yield return new Point(o.Left - Gap - size.Width, o.Top);     // solu
            yield return new Point(o.Left, o.Top - Gap - size.Height);    // üstü
        }

        double stepX = size.Width + Gap, stepY = size.Height + Gap;
        for (var dx = -maxRings; dx <= maxRings; dx++)
        for (var dy = -maxRings; dy <= maxRings; dy++)
            yield return new Point(desired.X + dx * stepX, desired.Y + dy * stepY);
    }

    private static bool IsFree(IEnumerable<Rect> occupied, Point location, Size size)
    {
        // Yarım boşluk payıyla kontrol et; böylece "sağı/altı" adayları tam Gap kadar uzakta kabul edilir
        var rect = new Rect(location.X - Gap / 2 + 1, location.Y - Gap / 2 + 1, size.Width + Gap - 2, size.Height + Gap - 2);
        return !occupied.Any(o => o.IntersectsWith(rect));
    }

    private static bool IsVisible(Rect? viewport, Point location, Size size) =>
        viewport is not { } v || v.Contains(new Rect(location, size));
}
