namespace KovanTerm.Services;

/// <summary>
/// Which palette color each project has (#145). A new project gets the color least used so far, preferring the one a
/// stable hash of its folder picks, so projects on the canvas stay apart; the choice is saved, so a project keeps its
/// color. Kept in settings (<see cref="Models.AppSettings.ProjectColors"/>).
/// </summary>
public sealed class ProjectColorMap(int paletteSize, IReadOnlyDictionary<string, int>? saved = null)
{
    /// <summary>Oldest projects are forgotten past this many.</summary>
    public const int MaxProjects = 200;

    private readonly Dictionary<string, int> _colors = new(
        (saved ?? new Dictionary<string, int>()).Where(p => p.Value >= 0 && p.Value < paletteSize),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The map in use (set at startup); the converter reads it.</summary>
    public static ProjectColorMap Current { get; set; } = new(8);

    public IReadOnlyDictionary<string, int> Colors => _colors;

    public int? Find(string? key) => key != null && _colors.TryGetValue(key, out var i) ? i : null;

    /// <summary>The project's color, given one if it's new; <c>true</c> in <paramref name="added"/> when it was.</summary>
    public int Assign(string key, out bool added)
    {
        added = false;
        if (_colors.TryGetValue(key, out var existing)) return existing;
        var uses = new int[paletteSize];
        foreach (var i in _colors.Values) uses[i]++;
        var preferred = Hash(key) % paletteSize;
        var least = uses.Min();
        // The hashed color if it's among the least used, otherwise the next least used one after it
        var pick = Enumerable.Range(0, paletteSize).Select(k => (preferred + k) % paletteSize).First(i => uses[i] == least);
        if (_colors.Count >= MaxProjects) _colors.Remove(_colors.Keys.First());
        _colors[key] = pick;
        added = true;
        return pick;
    }

    /// <summary>FNV-1a over the lower-cased key: string.GetHashCode changes with every run.</summary>
    public static int Hash(string key)
    {
        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(key.ToLowerInvariant()))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return (int)(hash & 0x7FFFFFFF);
    }
}
