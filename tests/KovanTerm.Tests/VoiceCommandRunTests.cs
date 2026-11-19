using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.Services.Dictation;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Voice commands carried out by the app (#21); what asks first and what doesn't.</summary>
public sealed class VoiceCommandRunTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public VoiceCommandRunTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class Dialogs(bool confirm) : IDialogService
    {
        public List<string> Asked { get; } = [];
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => false;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string action)
        {
            Asked.Add(message);
            return Task.FromResult(confirm);
        }
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(1);
    }

    private (MainViewModel Vm, List<(TerminalNodeViewModel Node, string Text)> Sent) Create(Dialogs dialogs, params NodeState[] nodes)
    {
        var store = new WorkspaceStore(Path.Combine(_dir, "workspace.json"));
        var w1 = Guid.NewGuid();
        store.Save(new WorkspaceState
        {
            Workspaces = [new WorkspaceInfo { Id = w1, Name = "Workspace 1" }, new WorkspaceInfo { Id = Guid.NewGuid(), Name = "Kovan" }],
            ActiveWorkspaceId = w1,
            Nodes = nodes.Select(n => n with { WorkspaceId = w1 }).ToList(),
        });
        var resolver = new CommandResolver(path: _dir, pathExt: ".exe");
        var vm = new MainViewModel(store, new ProfileStore(Path.Combine(_dir, "profiles.json")), resolver,
            new WslLauncher(resolver, () => ""), dialogs, new SettingsStore(Path.Combine(_dir, "settings.json")));
        var sent = new List<(TerminalNodeViewModel, string)>();
        vm.TerminalInputRequested += (node, text) => sent.Add((node, text));
        return (vm, sent);
    }

    private static NodeState Note(string title, double x = 0) =>
        new() { Title = title, CommandLine = "", Note = "text", X = x, Width = 400, Height = 300 };

    private static NodeState Terminal(string title, double x = 0) =>
        new() { Title = title, CommandLine = "claude.exe", X = x, Width = 400, Height = 300 };

    [Fact]
    public async Task GoTo_MakesItCurrent()
    {
        var (vm, _) = Create(new Dialogs(true), Note("Plan"), Note("Ideas", x: 600));
        TerminalNodeViewModel? went = null;
        vm.GoToNodeRequested += n => went = n;

        var result = await vm.RunVoiceCommandAsync("Ideas'a git");

        Assert.Equal("Ideas", went?.Title);
        Assert.Same(went, vm.CurrentNode);
        Assert.Equal("Went to Ideas.", result);
    }

    [Fact]
    public async Task Tell_AsksThenPastesAndSends()
    {
        var dialogs = new Dialogs(true);
        var (vm, sent) = Create(dialogs, Terminal("Claude"));

        var result = await vm.RunVoiceCommandAsync("Tell Claude to run the tests.");

        Assert.Contains("run the tests", Assert.Single(dialogs.Asked));
        var (node, text) = Assert.Single(sent);
        Assert.Equal("Claude", node.Title);
        Assert.Equal("\x1b[200~run the tests\x1b[201~\r", text);
        Assert.Equal("Sent to Claude.", result);
    }

    [Fact]
    public async Task Tell_NotConfirmed_SendsNothing()
    {
        var (vm, sent) = Create(new Dialogs(false), Terminal("Claude"));

        Assert.Equal("Not sent.", await vm.RunVoiceCommandAsync("Claude'a söyle testleri çalıştır"));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Close_AsksFirst()
    {
        var declined = Create(new Dialogs(false), Note("Plan"));
        Assert.Equal("Not closed.", await declined.Vm.RunVoiceCommandAsync("Close Plan"));
        Assert.Single(declined.Vm.Nodes);

        var accepted = Create(new Dialogs(true), Note("Plan"));
        await accepted.Vm.RunVoiceCommandAsync("Plan'ı kapat");
        Assert.Empty(accepted.Vm.Nodes);
    }

    [Fact]
    public async Task Allow_WithNobodyAsking_DoesNothing()
    {
        var dialogs = new Dialogs(true);
        var (vm, sent) = Create(dialogs, Terminal("Claude"));

        Assert.Equal("No agent is asking for permission.", await vm.RunVoiceCommandAsync("izin ver"));
        Assert.Empty(sent);
        Assert.Empty(dialogs.Asked);
    }

    [Fact]
    public async Task TwoWithTheSameName_AsksWhichOne()
    {
        var (vm, _) = Create(new Dialogs(true), Note("Plan"), Note("Plan", x: 600));
        TerminalNodeViewModel? went = null;
        vm.GoToNodeRequested += n => went = n;

        await vm.RunVoiceCommandAsync("go to Plan");

        // The fake picks the second button
        Assert.Same(vm.Nodes[1], went);
    }

    [Fact]
    public async Task NewNote_WithWhatWasSaid()
    {
        var (vm, _) = Create(new Dialogs(true));

        await vm.RunVoiceCommandAsync("Not al: yarın sürümü çıkar.");

        var note = Assert.Single(vm.Nodes);
        Assert.Equal("yarın sürümü çıkar", note.NoteText);
        Assert.False(note.IsEditingNote);
    }

    [Fact]
    public async Task Workspaces_ByNameAndInTurn()
    {
        var (vm, _) = Create(new Dialogs(true), Note("Plan"));

        await vm.RunVoiceCommandAsync("Kovan alanına geç");
        Assert.Equal("Kovan", vm.ActiveWorkspace.Name);
        await vm.RunVoiceCommandAsync("previous workspace");
        Assert.Equal("Workspace 1", vm.ActiveWorkspace.Name);
    }

    [Fact]
    public async Task Misheard_SaysWhatWasHeard()
    {
        var (vm, _) = Create(new Dialogs(true), Note("Plan"));

        Assert.StartsWith("Didn't understand \"Bugün hava güzel\"", await vm.RunVoiceCommandAsync("Bugün hava güzel"));
        Assert.Equal("Nothing is called \"copilot'a\" (\"Copilot'a git\").", await vm.RunVoiceCommandAsync("Copilot'a git"));
    }

    [Fact]
    public async Task FitAll_IsLeftToTheView()
    {
        var (vm, _) = Create(new Dialogs(true), Note("Plan"));
        var asked = new List<Services.Voice.VoiceCommandKind>();
        vm.ViewCommandRequested += (kind, _) => asked.Add(kind);

        await vm.RunVoiceCommandAsync("hepsini göster");

        Assert.Equal([Services.Voice.VoiceCommandKind.FitAll], asked);
    }

    // ---- Listening for a command ----

    private sealed class Recorder : IAudioRecorder
    {
        public void Start() { }
        public float[] Stop() => new float[SpeechModel.SampleRate];
    }

    private sealed class Models : ISpeechModelStore
    {
        public string PathOf(SpeechModel model) => model.FileName;
        public bool IsDownloaded(SpeechModel model) => true;
        public Task DownloadAsync(SpeechModel model, IProgress<double> progress, CancellationToken cancellation = default) => Task.CompletedTask;
    }

    private sealed class Whisper(string text) : ISpeechToText
    {
        public Task<string> TranscribeAsync(float[] samples, string language, string? prompt = null, CancellationToken cancellation = default) => Task.FromResult(text);
    }

    [Fact]
    public async Task ACommand_IsHandedOverInsteadOfTyped()
    {
        var dictation = new DictationViewModel(new Recorder(), new Models(), _ => new Whisper(" Go to the waiting agent."),
            new Dialogs(true), new SettingsStore(Path.Combine(_dir, "settings.json")));
        var typed = new List<string>();
        var heard = new List<string>();
        dictation.TextReady += (_, text) => typed.Add(text);
        dictation.CommandHeard += heard.Add;

        await dictation.ToggleCommandAsync();
        Assert.Contains("command", dictation.Status);
        await dictation.ToggleCommandAsync();

        Assert.Equal(["Go to the waiting agent."], heard);
        Assert.Empty(typed);
        Assert.Equal(DictationState.Idle, dictation.State);
    }
}
