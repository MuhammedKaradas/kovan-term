using System.IO;
using System.Windows;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Several workspaces (canvases) in one window: persisted, switched between, nodes moved across.</summary>
public sealed class WorkspacesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private string WorkspaceFile => Path.Combine(_dir, "workspace.json");

    public WorkspacesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class Dialogs(bool confirm) : IDialogService
    {
        public int Confirmations { get; private set; }
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => false;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string action)
        {
            Confirmations++;
            return Task.FromResult(confirm);
        }
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(null);
    }

    // No PATH and no WSL: nothing to resolve, so only notes and groups (which run nothing) are used
    private MainViewModel CreateViewModel(WorkspaceState? saved = null, IDialogService? dialogs = null)
    {
        var store = new WorkspaceStore(WorkspaceFile);
        if (saved != null) store.Save(saved);
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        return new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), dialogs ?? new Dialogs(confirm: true), new SettingsStore(Path.Combine(_dir, "settings.json")));
    }

    private static NodeState Note(string text, Guid? workspace = null, double x = 0) =>
        new() { Title = "Note", CommandLine = "", Note = text, WorkspaceId = workspace, X = x, Width = 400, Height = 300 };

    private static TerminalNodeViewModel NodeWithText(MainViewModel vm, string text) => vm.Nodes.Single(n => n.NoteText == text);

    [Fact]
    public void Restore_FileFromBeforeWorkspaces_PutsEverythingOnOneWorkspaceWithTheSavedViewport()
    {
        var vm = CreateViewModel(new WorkspaceState
        {
            Viewport = new ViewportState { Zoom = 0.5, X = 100, Y = -50 },
            Nodes = [Note("a"), Note("b", x: 600)],
        });

        var workspace = Assert.Single(vm.Workspaces);
        Assert.Equal("Workspace 1", workspace.Name);
        Assert.Same(workspace, vm.ActiveWorkspace);
        Assert.True(workspace.IsActive);
        Assert.Equal(0.5, vm.ViewportZoom);
        Assert.Equal(new Point(100, -50), vm.ViewportLocation);
        Assert.All(vm.Nodes, n =>
        {
            Assert.Equal(workspace.Id, n.WorkspaceId);
            Assert.True(n.IsInActiveWorkspace);
            Assert.Null(n.OtherWorkspaceName);
        });
    }

    [Fact]
    public void SaveAndRestore_KeepsWorkspacesTheirViewportsAndWhichIsShown()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a")] });
        var first = vm.ActiveWorkspace;
        vm.ViewportZoom = 0.8;
        vm.ViewportLocation = new Point(10, 20);
        vm.NewWorkspaceCommand.Execute(null);
        var second = vm.ActiveWorkspace;
        second.Name = "Backend";
        vm.ViewportZoom = 1.2;
        vm.ViewportLocation = new Point(-300, 40);
        vm.NewNoteCommand.Execute(new Point(0, 0));
        vm.SaveNow();

        var restored = CreateViewModel();

        Assert.Equal(["Workspace 1", "Backend"], restored.Workspaces.Select(w => w.Name));
        Assert.Equal(second.Id, restored.ActiveWorkspace.Id);
        Assert.Equal(1.2, restored.ViewportZoom);
        Assert.Equal(new Point(-300, 40), restored.ViewportLocation);
        Assert.Equal(0.8, restored.Workspaces[0].Zoom);
        Assert.Equal(new Point(10, 20), restored.Workspaces[0].Location);
        Assert.Equal(first.Id, NodeWithText(restored, "a").WorkspaceId);
        Assert.False(NodeWithText(restored, "a").IsInActiveWorkspace);
        Assert.Equal("Workspace 1", NodeWithText(restored, "a").OtherWorkspaceName);
        Assert.True(restored.Nodes.Single(n => n.WorkspaceId == second.Id).IsInActiveWorkspace);
        // Older versions read only the top-level viewport: it's the shown workspace's
        Assert.Equal(1.2, new WorkspaceStore(WorkspaceFile).Load()!.Viewport.Zoom);
    }

    [Fact]
    public void Restore_NodeOfUnknownWorkspace_GoesToTheFirst()
    {
        var known = new WorkspaceInfo { Name = "Main" };
        var vm = CreateViewModel(new WorkspaceState
        {
            Workspaces = [known, new WorkspaceInfo { Name = "Other" }],
            ActiveWorkspaceId = Guid.NewGuid(),
            Nodes = [Note("lost", Guid.NewGuid())],
        });

        Assert.Equal(known.Id, vm.ActiveWorkspace.Id);
        Assert.Equal(known.Id, NodeWithText(vm, "lost").WorkspaceId);
    }

    [Fact]
    public void Switch_HidesTheOtherWorkspacesNodesAndRestoresEachViewport()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a")] });
        var first = vm.ActiveWorkspace;
        vm.ViewportLocation = new Point(500, 500);
        vm.NewWorkspaceCommand.Execute(null);
        var second = vm.ActiveWorkspace;

        Assert.False(NodeWithText(vm, "a").IsInActiveWorkspace);
        Assert.Equal(new Point(0, 0), vm.ViewportLocation);
        Assert.True(second.IsRenaming);
        Assert.False(first.IsActive);

        vm.SwitchWorkspace(first);

        Assert.True(NodeWithText(vm, "a").IsInActiveWorkspace);
        Assert.Equal(new Point(500, 500), vm.ViewportLocation);
        Assert.Same(first, vm.SelectedWorkspace);
    }

    [Fact]
    public void Switch_BringsBackTheNodeLastWorkedWithThere()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a"), Note("b", x: 600)] });
        var first = vm.ActiveWorkspace;
        vm.CurrentNode = NodeWithText(vm, "b");
        vm.NewWorkspaceCommand.Execute(null);

        Assert.Null(vm.CurrentNode);
        vm.SwitchWorkspace(first);
        Assert.Same(NodeWithText(vm, "b"), vm.CurrentNode);
    }

    [Fact]
    public void NextAndPreviousWorkspace_WrapAround()
    {
        var vm = CreateViewModel(new WorkspaceState
        {
            Workspaces = [new WorkspaceInfo { Name = "A" }, new WorkspaceInfo { Name = "B" }, new WorkspaceInfo { Name = "C" }],
        });

        vm.PreviousWorkspaceCommand.Execute(null);
        Assert.Equal("C", vm.ActiveWorkspace.Name);
        vm.NextWorkspaceCommand.Execute(null);
        Assert.Equal("A", vm.ActiveWorkspace.Name);
        vm.NextWorkspaceCommand.Execute(null);
        Assert.Equal("B", vm.ActiveWorkspace.Name);
    }

    [Fact]
    public void NewWorkspace_GetsTheFirstFreeDefaultName_AndABlankRenameGetsOneBack()
    {
        var vm = CreateViewModel(new WorkspaceState
        {
            Workspaces = [new WorkspaceInfo { Name = "Workspace 1" }, new WorkspaceInfo { Name = "workspace 3" }],
        });

        vm.NewWorkspaceCommand.Execute(null);
        var added = vm.ActiveWorkspace;
        Assert.Equal("Workspace 2", added.Name);

        added.Name = "   ";
        vm.FinishRenaming(added);
        Assert.False(added.IsRenaming);
        Assert.Equal("Workspace 2", added.Name); // its own old name is free again

        added.Name = "  API  ";
        vm.FinishRenaming(added);
        Assert.Equal("API", added.Name);
    }

    [Fact]
    public void NewNodes_GoOnTheShownWorkspace_AndFreeSpotsIgnoreOtherWorkspaces()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a")] });
        vm.NewWorkspaceCommand.Execute(null);

        vm.NewNoteCommand.Execute(new Point(0, 0));

        var added = vm.Nodes.Single(n => n.NoteText == "" && n.IsNote);
        Assert.Equal(vm.ActiveWorkspace.Id, added.WorkspaceId);
        // "a" sits there too, but on the other workspace
        Assert.Equal(new Point(0, 0), added.Location);
    }

    [Fact]
    public void MoveToWorkspace_HidesTheNodeAndFindsItAFreeSpotThere()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a"), Note("b", x: 2000)] });
        var first = vm.ActiveWorkspace;
        vm.NewWorkspaceCommand.Execute(null);
        var second = vm.ActiveWorkspace;
        vm.SwitchWorkspace(first);
        vm.MoveToWorkspace(NodeWithText(vm, "a"), second);
        var b = NodeWithText(vm, "b");

        // b goes where a now is on the second workspace: it must not land on top of it
        b.Location = new Point(0, 0);
        vm.MoveToWorkspace(b, second);

        Assert.Equal(second.Id, NodeWithText(vm, "a").WorkspaceId);
        Assert.False(NodeWithText(vm, "a").IsInActiveWorkspace);
        Assert.Equal(second.Name, NodeWithText(vm, "a").OtherWorkspaceName);
        Assert.False(NodeWithText(vm, "a").Bounds.IntersectsWith(b.Bounds));
        Assert.DoesNotContain(second, vm.OtherWorkspaces(b));
    }

    [Fact]
    public void MoveGroupToWorkspace_TakesTheNodesInsideAlong()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("in"), Note("out", x: 3000)] });
        vm.AddGroup(new Point(), [NodeWithText(vm, "in")]);
        var group = vm.Nodes.Single(n => n.IsGroup);
        var first = vm.ActiveWorkspace;
        vm.NewWorkspaceCommand.Execute(null);
        var second = vm.ActiveWorkspace;
        vm.SwitchWorkspace(first);

        vm.MoveToWorkspace(group, second);

        Assert.Equal(second.Id, group.WorkspaceId);
        Assert.Equal(second.Id, NodeWithText(vm, "in").WorkspaceId);
        Assert.Equal(first.Id, NodeWithText(vm, "out").WorkspaceId);
    }

    [Fact]
    public void MoveToNewWorkspace_ShowsItAtTheSameViewWithTheNodeOnIt()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a"), Note("b", x: 3000)] });
        vm.ViewportLocation = new Point(-40, -40);
        var a = NodeWithText(vm, "a");

        vm.MoveToNewWorkspace(a);

        Assert.Equal(2, vm.Workspaces.Count);
        Assert.Equal(vm.ActiveWorkspace.Id, a.WorkspaceId);
        Assert.True(a.IsInActiveWorkspace);
        Assert.False(NodeWithText(vm, "b").IsInActiveWorkspace);
        Assert.Equal(new Point(-40, -40), vm.ViewportLocation);
        Assert.Equal(new Point(0, 0), a.Location);
        Assert.Same(a, vm.CurrentNode);
        Assert.True(vm.ActiveWorkspace.IsRenaming);
    }

    [Fact]
    public async Task CloseWorkspace_AfterConfirming_RemovesItAndItsNodes()
    {
        var dialogs = new Dialogs(confirm: true);
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("keep")] }, dialogs);
        var first = vm.ActiveWorkspace;
        vm.NewWorkspaceCommand.Execute(null);
        var second = vm.ActiveWorkspace;
        vm.NewNoteCommand.Execute(new Point());
        vm.Nodes.Last().NoteText = "gone";

        await vm.CloseWorkspaceCommand.ExecuteAsync(second);

        Assert.Equal(1, dialogs.Confirmations); // once for the workspace, not again for its note
        Assert.Equal([first], vm.Workspaces);
        Assert.Same(first, vm.ActiveWorkspace);
        Assert.Equal(["keep"], vm.Nodes.Select(n => n.NoteText));
        Assert.True(NodeWithText(vm, "keep").IsInActiveWorkspace);
    }

    [Fact]
    public async Task CloseWorkspace_NotConfirmed_KeepsEverything()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a")] }, new Dialogs(confirm: false));
        var first = vm.ActiveWorkspace;
        vm.NewWorkspaceCommand.Execute(null);

        await vm.CloseWorkspaceCommand.ExecuteAsync(first);

        Assert.Equal(2, vm.Workspaces.Count);
        Assert.Single(vm.Nodes);
    }

    [Fact]
    public async Task CloseWorkspace_TheLastOne_IsNotPossible()
    {
        var vm = CreateViewModel(new WorkspaceState { Nodes = [Note("a")] });

        await vm.CloseWorkspaceCommand.ExecuteAsync(vm.ActiveWorkspace);

        Assert.Single(vm.Workspaces);
        Assert.Single(vm.Nodes);
    }

    [Fact]
    public void BadgeText_PrefersWaitingOverWorking()
    {
        var w = new WorkspaceViewModel { Name = "A" };
        Assert.Null(w.BadgeText);
        w.WorkingCount = 2;
        Assert.Equal("2 working", w.BadgeText);
        w.WaitingCount = 1;
        Assert.Equal("1 waiting", w.BadgeText);
        Assert.True(w.HasWaiting);
    }
}
