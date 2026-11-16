using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>A row in the Recent conversations dialog.</summary>
public sealed class ConversationRow(ConversationInfo info, bool isOpen, DateTime now)
{
    public ConversationInfo Info { get; } = info;
    public string Title => Info.Title;
    public string FolderName => Info.Folder is { } f ? Path.GetFileName(Path.TrimEndingDirectorySeparator(f)) is { Length: > 0 } n ? n : f : "(unknown folder)";
    public string? Folder => Info.Folder;
    /// <summary>Already open in a node on the canvas.</summary>
    public bool IsOpen { get; } = isOpen;
    public string When { get; } = Ago(info.LastActive, now);

    public static string Ago(DateTime then, DateTime now)
    {
        var d = now - then;
        return d.TotalMinutes < 1 ? "just now"
            : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago"
            : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago"
            : d.TotalDays < 7 ? $"{(int)d.TotalDays} d ago"
            : then.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}

/// <summary>"Recent conversations": pick a saved Claude Code conversation to reopen as a node.</summary>
public partial class RecentConversationsViewModel : ObservableObject
{
    private readonly IReadOnlyList<ConversationRow> _all;

    public RecentConversationsViewModel(IEnumerable<ConversationInfo> conversations, IReadOnlySet<string> openSessionIds, DateTime now)
    {
        _all = conversations.Select(c => new ConversationRow(c, openSessionIds.Contains(c.SessionId), now)).ToList();
        Rows = _all;
        Selected = Rows.FirstOrDefault(r => !r.IsOpen);
    }

    [ObservableProperty]
    public partial IReadOnlyList<ConversationRow> Rows { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial ConversationRow? Selected { get; set; }

    /// <summary>Filters by title or folder; every word must appear.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = "";

    partial void OnSearchChanged(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Rows = words.Length == 0
            ? _all
            : _all.Where(r => words.All(w => r.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                             || (r.Folder?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
        if (Selected == null || !Rows.Contains(Selected)) Selected = Rows.FirstOrDefault();
    }

    public string? EmptyMessage => _all.Count == 0 ? "No saved Claude Code conversations were found." : null;

    /// <summary>The view watches this to close the dialog: <c>true</c> open, <c>false</c> cancel.</summary>
    [ObservableProperty]
    public partial bool? DialogResult { get; set; }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => DialogResult = true;

    private bool CanOpen() => Selected is { Folder: not null };

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
