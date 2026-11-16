using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>A note left on a diff line, to be sent to the agent.</summary>
public sealed record ReviewNote(string Path, int? Line, string Code, string Text)
{
    public string Location => Line is { } l ? $"{Path}:{l}" : Path;
}

/// <summary>
/// The Changes panel: the git changes in the current node's folder, the selected file's diff and the user's notes
/// on its lines. Notes are pasted into the node's terminal as one message, for the user to review and send.
/// </summary>
public partial class ChangesViewModel : ObservableObject
{
    private readonly GitChanges _git;
    private int _refreshVersion;

    public ChangesViewModel(GitChanges git) => _git = git;

    /// <summary>Raised with the message to paste into the node's terminal.</summary>
    public event Action<TerminalNodeViewModel, string>? SendRequested;

    public ObservableCollection<ChangedFile> Files { get; } = new();
    public ObservableCollection<ReviewNote> Notes { get; } = new();

    [ObservableProperty]
    public partial TerminalNodeViewModel? Node { get; private set; }

    [ObservableProperty]
    public partial ChangedFile? SelectedFile { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DiffLine> DiffLines { get; private set; } = [];

    /// <summary>Why there's nothing to show (not a repository, no changes…); <c>null</c> otherwise.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; } = "Select a terminal to see its changes.";

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>The line a note is being written for; <c>null</c> when the note box is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingNote))]
    public partial DiffLine? NoteLine { get; set; }

    public bool IsEditingNote => NoteLine != null;

    [ObservableProperty]
    public partial string NoteText { get; set; } = "";

    public bool HasNotes => Notes.Count > 0;

    public string SendNotesLabel => Notes.Count == 1 ? "Send 1 note to the agent" : $"Send {Notes.Count} notes to the agent";

    /// <summary>Shows the given node's changes (or clears the panel).</summary>
    public async Task ShowAsync(TerminalNodeViewModel? node)
    {
        if (node != Node)
        {
            Node = node;
            Notes.Clear();
            OnNotesChanged();
        }
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        if (Node?.WorkingDirectory is not { Length: > 0 } dir)
        {
            Files.Clear();
            DiffLines = [];
            Message = Node == null ? "Select a terminal to see its changes." : "This terminal has no working folder.";
            return;
        }
        IsBusy = true;
        var status = await _git.StatusAsync(dir, Node.Worktree?.BaseCommit);
        if (version != _refreshVersion) return; // a newer refresh started meanwhile
        IsBusy = false;
        if (status.Value == null)
        {
            Files.Clear();
            DiffLines = [];
            Message = status.Error;
            return;
        }

        var previous = SelectedFile?.Path;
        Files.Clear();
        foreach (var f in status.Value) Files.Add(f);
        Message = Files.Count == 0 ? "No changes." : null;
        // Keep the selection across refreshes if the file is still changed. Selecting another file loads its diff
        // (OnSelectedFileChanged); the same one is reloaded here, since its content may have changed.
        var selection = Files.FirstOrDefault(f => f.Path == previous) ?? Files.FirstOrDefault();
        if (Equals(selection, SelectedFile))
        {
            if (selection != null) await LoadDiffAsync(selection);
            else DiffLines = [];
        }
        else
        {
            SelectedFile = selection;
            if (selection == null) DiffLines = [];
        }
    }

    partial void OnSelectedFileChanged(ChangedFile? value)
    {
        NoteLine = null;
        if (value != null) _ = LoadDiffAsync(value);
    }

    private ChangedFile? _diffLoadedFor;

    private async Task LoadDiffAsync(ChangedFile file)
    {
        if (Node?.WorkingDirectory is not { } dir) return;
        var version = _refreshVersion;
        _diffLoadedFor = file;
        var diff = await _git.DiffAsync(dir, file, Node.Worktree?.BaseCommit);
        if (version != _refreshVersion || _diffLoadedFor != file) return;
        DiffLines = diff.Value is { } text
            ? DiffParser.Parse(text).Where(l => l.Kind != DiffLineKind.Header).ToList()
            : [new DiffLine(DiffLineKind.Note, diff.Error ?? "Couldn't read the diff.", null, null)];
    }

    // ---- Notes ----

    [RelayCommand]
    private void StartNote(DiffLine line)
    {
        if (!line.CanAnnotate) return;
        NoteLine = line;
        NoteText = "";
    }

    [RelayCommand]
    private void AddNote()
    {
        if (NoteLine is not { } line || SelectedFile is not { } file || string.IsNullOrWhiteSpace(NoteText)) return;
        Notes.Add(new ReviewNote(file.Path, line.Line, line.Text.Trim(), NoteText.Trim()));
        NoteLine = null;
        NoteText = "";
        OnNotesChanged();
    }

    [RelayCommand]
    private void CancelNote() => NoteLine = null;

    [RelayCommand]
    private void RemoveNote(ReviewNote note)
    {
        Notes.Remove(note);
        OnNotesChanged();
    }

    [RelayCommand]
    private void SendNotes()
    {
        if (Node == null || Notes.Count == 0) return;
        SendRequested?.Invoke(Node, ComposeMessage(Notes));
        Notes.Clear();
        OnNotesChanged();
    }

    private void OnNotesChanged()
    {
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(SendNotesLabel));
    }

    /// <summary>The message the agent gets: one bullet per note with its location and the line's code.</summary>
    public static string ComposeMessage(IEnumerable<ReviewNote> notes)
    {
        var sb = new StringBuilder("Review notes on your changes:\n");
        foreach (var n in notes)
        {
            sb.Append("\n- ").Append(n.Location);
            if (n.Code.Length > 0) sb.Append(" `").Append(n.Code.Length > 120 ? n.Code[..120] + "…" : n.Code).Append('`');
            sb.Append(": ").Append(n.Text.Replace("\r\n", "\n").Replace("\n", "\n  "));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The text as a bracketed paste: the agent's prompt takes it as one block (newlines don't submit it),
    /// and nothing is sent until the user presses Enter.
    /// </summary>
    public static string BracketedPaste(string text) => "\x1b[200~" + text.Replace("\x1b", "") + "\x1b[201~";
}
