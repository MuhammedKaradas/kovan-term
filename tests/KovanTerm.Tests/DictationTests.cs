using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.Services.Dictation;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>Dictation into a terminal (#20); the microphone and Whisper are faked.</summary>
public sealed class DictationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));

    public DictationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class Recorder(float[] samples) : IAudioRecorder
    {
        public bool Recording { get; private set; }
        public Exception? FailWith { get; init; }
        public void Start()
        {
            if (FailWith != null) throw FailWith;
            Recording = true;
        }
        public float[] Stop()
        {
            Recording = false;
            return samples;
        }
    }

    private sealed class Models(bool downloaded) : ISpeechModelStore
    {
        public bool Downloaded { get; private set; } = downloaded;
        public List<string> Downloads { get; } = [];
        public string PathOf(SpeechModel model) => "models/" + model.FileName;
        public bool IsDownloaded(SpeechModel model) => Downloaded;
        public Task DownloadAsync(SpeechModel model, IProgress<double> progress, CancellationToken cancellation = default)
        {
            Downloads.Add(model.Name);
            Downloaded = true;
            return Task.CompletedTask;
        }
    }

    private sealed class Whisper(string text) : ISpeechToText
    {
        public List<(int Samples, string Language)> Calls { get; } = [];
        public Task<string> TranscribeAsync(float[] samples, string language, CancellationToken cancellation = default)
        {
            Calls.Add((samples.Length, language));
            return Task.FromResult(text);
        }
    }

    private sealed class Dialogs(bool confirm) : IDialogService
    {
        public List<string> Messages { get; } = [];
        public int Confirmations { get; private set; }
        public bool ShowNewTerminal(NewTerminalViewModel viewModel) => false;
        public bool ShowRecentConversations(RecentConversationsViewModel viewModel) => false;
        public string? PickFolder(string? initialDirectory) => null;
        public Task<WorktreeCloseChoice> AskCloseWorktreeAsync(string title, string message) => Task.FromResult(WorktreeCloseChoice.Cancel);
        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmAsync(string title, string message, string action)
        {
            Confirmations++;
            return Task.FromResult(confirm);
        }
        public void OpenInBrowser(string url) { }
        public Task<int?> ChooseAsync(string title, string message, params string[] buttons) => Task.FromResult<int?>(null);
    }

    private static readonly float[] OneSecond = new float[SpeechModel.SampleRate];

    private (DictationViewModel Vm, List<(object Node, string Text)> Typed) Create(
        IAudioRecorder recorder, ISpeechModelStore models, ISpeechToText whisper, IDialogService dialogs, AppSettings? settings = null)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        if (settings != null) store.Save(settings);
        var vm = new DictationViewModel(recorder, models, _ => whisper, dialogs, store);
        var typed = new List<(object, string)>();
        vm.TextReady += (node, text) => typed.Add((node, text));
        return (vm, typed);
    }

    [Fact]
    public async Task PressSpeakPress_TypesWhatWasSaidIntoTheTerminalRecordingStartedFor()
    {
        var recorder = new Recorder(OneSecond);
        var whisper = new Whisper(" Run the tests and fix\n what fails.");
        var (vm, typed) = Create(recorder, new Models(downloaded: true), whisper, new Dialogs(true),
            new AppSettings { DictationLanguage = "tr" });
        var first = new object();

        await vm.ToggleAsync(first, "Claude");
        Assert.True(recorder.Recording);
        Assert.Equal(DictationState.Recording, vm.State);
        Assert.Contains("Claude", vm.Status);

        await vm.ToggleAsync(new object(), "Other"); // the current terminal changed meanwhile

        Assert.Equal(DictationState.Idle, vm.State);
        Assert.Equal([(first, "Run the tests and fix what fails.")], typed);
        Assert.Equal([(SpeechModel.SampleRate, "tr")], whisper.Calls);
    }

    [Fact]
    public async Task WithoutATerminal_NothingIsRecorded()
    {
        var recorder = new Recorder(OneSecond);
        var (vm, _) = Create(recorder, new Models(true), new Whisper("x"), new Dialogs(true));

        await vm.ToggleAsync(null, null);

        Assert.False(recorder.Recording);
        Assert.Equal(DictationState.Idle, vm.State);
        Assert.Contains("Select a terminal", vm.Status);
    }

    [Fact]
    public async Task TheModel_IsDownloadedOnlyAfterAsking()
    {
        var models = new Models(downloaded: false);
        var dialogs = new Dialogs(confirm: false);
        var recorder = new Recorder(OneSecond);
        var (vm, _) = Create(recorder, models, new Whisper("x"), dialogs, new AppSettings { DictationModel = "small" });

        await vm.ToggleAsync(new object(), "Claude");
        Assert.Equal(1, dialogs.Confirmations);
        Assert.Empty(models.Downloads);
        Assert.False(recorder.Recording);

        var (vm2, _) = Create(recorder, models, new Whisper("x"), new Dialogs(confirm: true));
        await vm2.ToggleAsync(new object(), "Claude");
        Assert.Equal(["small"], models.Downloads);
        // Recording starts on the next press
        Assert.False(recorder.Recording);
        Assert.Contains("ready", vm2.Status);
    }

    [Fact]
    public async Task NothingHeard_NothingTyped()
    {
        var (vm, typed) = Create(new Recorder(OneSecond), new Models(true), new Whisper(" [BLANK_AUDIO]"), new Dialogs(true));

        await vm.ToggleAsync(new object(), "Claude");
        await vm.ToggleAsync(null, null);

        Assert.Empty(typed);
        Assert.Contains("Didn't catch", vm.Status);
    }

    [Fact]
    public async Task ATap_IsTooShortToTranscribe()
    {
        var whisper = new Whisper("x");
        var (vm, typed) = Create(new Recorder(new float[1000]), new Models(true), whisper, new Dialogs(true));

        await vm.ToggleAsync(new object(), "Claude");
        await vm.ToggleAsync(null, null);

        Assert.Empty(typed);
        Assert.Empty(whisper.Calls);
    }

    [Fact]
    public async Task NoMicrophone_IsReported()
    {
        var dialogs = new Dialogs(true);
        var recorder = new Recorder(OneSecond) { FailWith = new InvalidOperationException("No microphone was found.") };
        var (vm, _) = Create(recorder, new Models(true), new Whisper("x"), dialogs);

        await vm.ToggleAsync(new object(), "Claude");

        Assert.Equal(DictationState.Idle, vm.State);
        Assert.Equal(["No microphone was found."], dialogs.Messages);
    }

    [Theory]
    [InlineData(" Hello world.", "Hello world.")]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData(" (music) Testleri çalıştır. *coughs*", "Testleri çalıştır.")]
    [InlineData("first line\n second line", "first line second line")]
    public void Text_IsOneLineOfSpeech(string heard, string typed) => Assert.Equal(typed, DictationText.Clean(heard));

    [Fact]
    public void Models_FallBackToBase()
    {
        Assert.Equal("small", SpeechModel.Find("Small").Name);
        Assert.Equal("base", SpeechModel.Find("large-v9").Name);
        Assert.Equal("base", SpeechModel.Find(null).Name);
        Assert.Equal("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin", SpeechModel.Find("base").Url);
    }

    [Fact]
    public void WavFile_IsReadAsMono16kHz()
    {
        // 0.1 s of 32 kHz stereo: left 0.5, right -0.25
        var frames = 3200;
        using var data = new MemoryStream();
        using (var w = new BinaryWriter(data, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF".ToCharArray()); w.Write(36 + frames * 4); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(32000); w.Write(32000 * 4);
            w.Write((short)4); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(frames * 4);
            for (var i = 0; i < frames; i++) { w.Write((short)16384); w.Write((short)-8192); }
        }
        data.Position = 0;

        var samples = WavFile.Read(data);

        Assert.Equal(1600, samples.Length);
        Assert.All(samples, s => Assert.Equal(0.125f, s, 4));
    }
}
