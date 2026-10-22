using System.Windows;

namespace KovanTerm.ViewModels;

/// <summary>Keyboard navigation order between terminals: row by row, left to right within each row.</summary>
public static class NodeNavigation
{
    /// <summary>Nodes whose top edges are this close are counted as the same row (canvas units).</summary>
    public const double RowTolerance = 120;

    /// <summary>Sorts nodes into reading order.</summary>
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
    /// The node after (or before) <paramref name="current"/>; wraps around at the end.
    /// If no node is focused, starts from the node closest to <paramref name="viewportCenter"/>.
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
