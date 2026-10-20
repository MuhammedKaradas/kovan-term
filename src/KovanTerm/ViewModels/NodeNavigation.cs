using System.Windows;

namespace KovanTerm.ViewModels;

/// <summary>Terminaller arasında klavyeyle gezinme sırası: satır satır, her satırda soldan sağa.</summary>
public static class NodeNavigation
{
    /// <summary>Üst kenarları bu kadar yakın olan düğümler aynı satırda sayılır (tuval birimi).</summary>
    public const double RowTolerance = 120;

    /// <summary>Düğümleri okuma sırasına dizer.</summary>
    public static IReadOnlyList<T> ReadingOrder<T>(IEnumerable<T> nodes, Func<T, Rect> bounds)
    {
        var rows = new List<List<T>>();
        double rowTop = double.NegativeInfinity;
        foreach (var node in nodes.OrderBy(n => bounds(n).Top))
        {
            if (bounds(node).Top - rowTop > RowTolerance)
            {
                rows.Add([]);
                rowTop = bounds(node).Top;
            }
            rows[^1].Add(node);
        }
        return rows.SelectMany(r => r.OrderBy(n => bounds(n).Left)).ToList();
    }

    /// <summary>
    /// <paramref name="current"/>'tan sonraki (ya da önceki) düğüm; sona gelince başa döner.
    /// Odakta düğüm yoksa <paramref name="viewportCenter"/>'a en yakın düğümden başlanır.
    /// </summary>
    public static T? Next<T>(IEnumerable<T> nodes, Func<T, Rect> bounds, T? current, bool forward, Point viewportCenter) where T : class
    {
        var order = ReadingOrder(nodes, bounds);
        if (order.Count == 0) return null;

        var index = current == null ? -1 : IndexOf(order, current);
        if (index < 0)
            return order.MinBy(n => (Center(bounds(n)) - viewportCenter).LengthSquared);

        var step = forward ? 1 : -1;
        return order[(index + step + order.Count) % order.Count];
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    private static Point Center(Rect r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
}
