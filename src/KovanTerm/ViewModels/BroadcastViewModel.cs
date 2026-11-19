using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KovanTerm.ViewModels;

/// <summary>A terminal the prompt can go to, and whether it will.</summary>
public sealed partial class BroadcastTarget(TerminalNodeViewModel node, bool isChecked) : ObservableObject
{
    public TerminalNodeViewModel Node { get; } = node;
    public string Title => Node.Title;
    /// <summary>What it's doing, e.g. "Working", or where it is if it's on another workspace.</summary>
    public string Detail =>
        string.Join("  ·  ", new[] { Node.StatusText ?? (string.IsNullOrEmpty(Node.AgentKind) ? "Terminal" : Node.Status.ToString()), Node.OtherWorkspaceName }
            .Where(s => !string.IsNullOrEmpty(s)));

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}

/// <summary>"Send to agents" (#78): one prompt, pasted into several terminals.</summary>
public sealed partial class BroadcastViewModel : ObservableObject
{
    public ObservableCollection<BroadcastTarget> Targets { get; }

    public BroadcastViewModel(IEnumerable<BroadcastTarget> targets)
    {
        Targets = new ObservableCollection<BroadcastTarget>(targets);
        foreach (var t in Targets)
            t.PropertyChanged += (_, _) =>
            {
                SendCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SendText));
            };
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Text { get; set; } = "";

    /// <summary>Submit it too (Enter after the paste); otherwise it waits in each terminal for review.</summary>
    [ObservableProperty]
    public partial bool PressEnter { get; set; } = true;

    [ObservableProperty]
    public partial bool? DialogResult { get; set; }

    public IReadOnlyList<TerminalNodeViewModel> Checked => Targets.Where(t => t.IsChecked).Select(t => t.Node).ToList();

    public string SendText => Checked.Count switch
    {
        0 => "Send",
        1 => "Send to 1",
        var n => $"Send to {n}",
    };

    private bool CanSend() => Text.Trim().Length > 0 && Targets.Any(t => t.IsChecked);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send() => DialogResult = true;

    [RelayCommand]
    private void Cancel() => DialogResult = false;

    [RelayCommand]
    private void CheckAll()
    {
        var all = !Targets.All(t => t.IsChecked);
        foreach (var t in Targets) t.IsChecked = all;
    }
}
