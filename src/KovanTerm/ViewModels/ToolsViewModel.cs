using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>One kind of tool for one agent, e.g. "Skills (12)", with the items the filter lets through.</summary>
public sealed record ToolGroup(string Agent, ToolKind Kind, string Title, IReadOnlyList<ToolItem> Items);

/// <summary>
/// The Tools window (#139): what Claude Code and Codex have set up, for the user and the current terminal's project.
/// Read only; the list is read in the background.
/// </summary>
public sealed partial class ToolsViewModel(Func<string?, IReadOnlyList<ToolItem>> scan, string? project) : ObservableObject
{
    private IReadOnlyList<ToolItem> _all = [];

    public IReadOnlyList<string> Agents { get; } = [ToolsInventory.Claude, ToolsInventory.Codex];

    /// <summary>The project folder whose project and local definitions are included; <c>null</c>: the user's only.</summary>
    public string? Project { get; } = project;

    public string ProjectText => Project is { } p ? $"User and {p}" : "User (open a terminal in a project to include its own)";

    [ObservableProperty]
    public partial string Agent { get; set; } = ToolsInventory.Claude;

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<ToolGroup> Groups { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>"Claude Code: 4 MCP servers · 12 skills…", or why the list is empty.</summary>
    [ObservableProperty]
    public partial string? Summary { get; private set; }

    partial void OnAgentChanged(string value) => Apply();
    partial void OnFilterChanged(string value) => Apply();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        _all = await Task.Run(() => scan(Project));
        IsLoading = false;
        Apply();
    }

    private void Apply()
    {
        var f = Filter.Trim();
        var groups = new List<ToolGroup>();
        foreach (var kind in Enum.GetValues<ToolKind>())
        {
            var all = _all.Where(i => i.Agent == Agent && i.Kind == kind).ToList();
            if (all.Count == 0) continue;
            var shown = f.Length == 0 ? all
                : all.Where(i => i.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || i.Detail.Contains(f, StringComparison.OrdinalIgnoreCase)
                                 || i.Source.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
            if (shown.Count == 0) continue;
            var count = shown.Count == all.Count ? $"{all.Count}" : $"{shown.Count} of {all.Count}";
            groups.Add(new ToolGroup(Agent, kind, $"{all[0].KindText} ({count})", shown));
        }
        Groups = groups;
        var mine = _all.Where(i => i.Agent == Agent).ToList();
        Summary = IsLoading ? null
            : mine.Count == 0 ? $"Nothing set up for {Agent} here."
            : groups.Count == 0 ? "Nothing matches the filter."
            : null;
    }
}
