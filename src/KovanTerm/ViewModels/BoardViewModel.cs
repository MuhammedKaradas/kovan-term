using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services.Mesh;

namespace KovanTerm.ViewModels;

/// <summary>A task card on the Board panel.</summary>
public sealed class TaskCard(MeshTask task)
{
    public MeshTask Task { get; } = task;
    public string Title => $"#{Task.Id} {Task.Title}";
    public string? Detail => Task.Detail;
    public string? Owner => Task switch
    {
        { OwnerName: null } => null,
        { Assigned: true, State: "todo" } => $"Given to {Task.OwnerName}; moves on when it starts working",
        { Assigned: true, State: "doing" } => $"{Task.OwnerName}; done when its turn ends",
        _ => Task.OwnerName,
    };
    public string State => Task.State;
    /// <summary>Can be given to an agent (not while one is working on it).</summary>
    public bool CanAssign => Task.State != "doing";
    public string? Note => Task.Note;
    public bool CanStart => Task.State == "todo";
    public bool CanFinish => Task.State == "doing";
    public bool CanReopen => Task.State != "todo";
}

/// <summary>A message line on the Board panel: "10:42 Claude (a1b2) → Codex (77f0): …".</summary>
public sealed class MessageLine(MeshMessage m)
{
    public string Header => $"{m.At:HH:mm}  {m.FromName} → {m.ToName ?? "all"}";
    public string Text => m.Text;
}

/// <summary>
/// The Board panel: the task board the agents share (the user can add and move tasks too), the messages they sent
/// each other and the files they leased. Rebuilt from <see cref="MeshBoard"/> on every change (it's small).
/// </summary>
public partial class BoardViewModel(MeshBoard board) : ObservableObject
{
    public ObservableCollection<TaskCard> Todo { get; } = new();
    public ObservableCollection<TaskCard> Doing { get; } = new();
    public ObservableCollection<TaskCard> Done { get; } = new();
    public ObservableCollection<MessageLine> Messages { get; } = new();
    public ObservableCollection<string> Leases { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial string NewTaskTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool HasLeases { get; private set; }

    [ObservableProperty]
    public partial bool HasMessages { get; private set; }

    /// <summary>Open tasks on the tab, e.g. " · 3"; empty when there are none.</summary>
    [ObservableProperty]
    public partial string Badge { get; private set; } = "";

    /// <summary>Call on the UI thread after the board changed.</summary>
    public void Refresh()
    {
        Fill(Todo, board.Tasks.Where(t => t.State == "todo").OrderBy(t => t.Id));
        Fill(Doing, board.Tasks.Where(t => t.State == "doing").OrderBy(t => t.Id));
        Fill(Done, board.Tasks.Where(t => t.State == "done").OrderByDescending(t => t.Updated).Take(30));
        Messages.Clear();
        foreach (var m in board.Messages.TakeLast(60)) Messages.Add(new MessageLine(m));
        HasMessages = Messages.Count > 0;
        Leases.Clear();
        foreach (var l in board.Leases) Leases.Add($"{string.Join(", ", l.Paths)} — {l.OwnerName}, until {l.Expires:HH:mm}");
        HasLeases = Leases.Count > 0;
        var open = Todo.Count + Doing.Count;
        Badge = open > 0 ? $" · {open}" : "";
    }

    private static void Fill(ObservableCollection<TaskCard> target, IEnumerable<MeshTask> tasks)
    {
        target.Clear();
        foreach (var t in tasks) target.Add(new TaskCard(t));
    }

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private void AddTask()
    {
        board.CreateTask(NewTaskTitle, null);
        NewTaskTitle = "";
        Services.UsageStats.Count("feature.board");
    }

    private bool CanAddTask() => !string.IsNullOrWhiteSpace(NewTaskTitle);

    [RelayCommand]
    private void StartTask(TaskCard card) => board.MoveTask(card.Task.Id, "doing");

    [RelayCommand]
    private void FinishTask(TaskCard card) => board.MoveTask(card.Task.Id, "done");

    [RelayCommand]
    private void ReopenTask(TaskCard card) => board.MoveTask(card.Task.Id, "todo");

    [RelayCommand]
    private void DeleteTask(TaskCard card) => board.DeleteTask(card.Task.Id);

    /// <summary>A card was dropped on a column (<c>todo</c>, <c>doing</c>, <c>done</c>).</summary>
    public void Drop(TaskCard card, string state)
    {
        if (card.Task.State != state) board.MoveTask(card.Task.Id, state);
    }
}
