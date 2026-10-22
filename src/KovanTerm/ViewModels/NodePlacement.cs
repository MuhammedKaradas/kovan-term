using System.Windows;

namespace KovanTerm.ViewModels;

/// <summary>
/// Finds a free spot for a new node. Terminals are HWNDs, so overlapping nodes cover each other's
/// headers (airspace); that's why a new node is placed so it doesn't intersect any node.
/// </summary>
public static class NodePlacement
{
    public const double Gap = 40;

    /// <summary>
    /// Returns <paramref name="desired"/> if it's free, otherwise the nearest free location.
    /// Candidates: a grid around the desired point and the right/bottom/left/top of existing nodes.
    /// If <paramref name="viewport"/> is given, fully visible candidates are preferred.
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
            if (!IsVisible(viewport, candidate, size)) score += 1e12; // an off-screen spot only as a last resort
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
            yield return new Point(o.Right + Gap, o.Top);                 // right
            yield return new Point(o.Left, o.Bottom + Gap);               // below
            yield return new Point(o.Left - Gap - size.Width, o.Top);     // left
            yield return new Point(o.Left, o.Top - Gap - size.Height);    // above
        }

        double stepX = size.Width + Gap, stepY = size.Height + Gap;
        for (var dx = -maxRings; dx <= maxRings; dx++)
        for (var dy = -maxRings; dy <= maxRings; dy++)
            yield return new Point(desired.X + dx * stepX, desired.Y + dy * stepY);
    }

    private static bool IsFree(IEnumerable<Rect> occupied, Point location, Size size)
    {
        // Check with a half-gap margin so "right/below" candidates exactly Gap away are accepted
        var rect = new Rect(location.X - Gap / 2 + 1, location.Y - Gap / 2 + 1, size.Width + Gap - 2, size.Height + Gap - 2);
        return !occupied.Any(o => o.IntersectsWith(rect));
    }

    private static bool IsVisible(Rect? viewport, Point location, Size size) =>
        viewport is not { } v || v.Contains(new Rect(location, size));
}
