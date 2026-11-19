using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Services;
using KovanTerm.Services.Dictation;

namespace KovanTerm.ViewModels;

public enum DictationState
{
    Idle,
    Downloading,
    Recording,
    Transcribing,
}

/// <summary>
/// Dictation (#20): press the shortcut, speak, press it again, and what was said is typed into the terminal that was
/// current when recording started. Speech is transcribed on this computer; the model is downloaded once, after asking.
/// </summary>
public sealed partial class DictationViewModel(
    IAudioRecorder recorder,
    ISpeechModelStore models,
    Func<string, ISpeechToText> speechToText,
    IDialogService dialogs,
    SettingsStore settings,
    Action<string>? log = null) : ObservableObject
{
    /// <summary>Recording stops by itself after this long.</summary>
    public static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(2);
    /// <summary>Shorter than this is a slip of the finger.</summary>
    private const int MinSamples = SpeechModel.SampleRate / 4;
    public const string ShortcutText = "Ctrl+Shift+Space";

    /// <summary>Dictated text to type into a terminal node.</summary>
    public event Action<object, string>? TextReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording), nameof(IsBusy))]
    private DictationState _state;

    /// <summary>What dictation is doing, for the toolbar; empty when there's nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    public bool IsRecording => State == DictationState.Recording;
    public bool IsBusy => State is DictationState.Downloading or DictationState.Transcribing;
    public bool HasStatus => Status.Length > 0;

    private object? _target;
    private string? _targetName;
    private ISpeechToText? _engine;
    private string? _enginePath;
    private CancellationTokenSource? _recordingLimit;
    private int _statusVersion;

    /// <summary>
    /// Starts recording for <paramref name="target"/> (named <paramref name="targetName"/> in the status), or stops and
    /// types what was said into the terminal recording started for.
    /// </summary>
    public async Task ToggleAsync(object? target, string? targetName)
    {
        switch (State)
        {
            case DictationState.Idle:
                await StartAsync(target, targetName);
                break;
            case DictationState.Recording:
                await StopAsync();
                break;
        }
    }

    private async Task StartAsync(object? target, string? targetName)
    {
        if (target == null)
        {
            ShowBriefly("Select a terminal to dictate into first.");
            return;
        }
        var model = SpeechModel.Find(settings.Load().DictationModel);
        if (!models.IsDownloaded(model))
        {
            if (!await DownloadAsync(model)) return;
            // The download took a while; the user may have moved on, so recording starts on the next press
            ShowBriefly($"Speech model ready. Press {ShortcutText} and speak.");
            return;
        }
        try
        {
            recorder.Start();
        }
        catch (InvalidOperationException ex)
        {
            await dialogs.ShowMessageAsync("Dictation", ex.Message);
            return;
        }
        _target = target;
        _targetName = targetName;
        State = DictationState.Recording;
        SetStatus($"Listening for {targetName}… press {ShortcutText} to stop");
        _recordingLimit = new CancellationTokenSource();
        _ = StopAfterLimitAsync(_recordingLimit.Token);
    }

    private async Task StopAfterLimitAsync(CancellationToken cancellation)
    {
        try { await Task.Delay(MaxRecording, cancellation); }
        catch (TaskCanceledException) { return; }
        if (State == DictationState.Recording) await StopAsync();
    }

    private async Task<bool> DownloadAsync(SpeechModel model)
    {
        if (!await dialogs.ConfirmAsync("Dictation",
                $"Dictation needs a speech model (Whisper \"{model.Name}\", {model.SizeText}), downloaded once from Hugging Face.\n\n" +
                "Speech is turned into text on this computer; audio never leaves it.", "Download"))
            return false;
        State = DictationState.Downloading;
        var progress = new Progress<double>(p => SetStatus($"Downloading the speech model… {p:P0}"));
        try
        {
            await models.DownloadAsync(model, progress);
            return true;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or TaskCanceledException)
        {
            SetStatus("");
            await dialogs.ShowMessageAsync("Dictation", $"The speech model couldn't be downloaded:\n{ex.Message}");
            return false;
        }
        finally
        {
            State = DictationState.Idle;
        }
    }

    private async Task StopAsync()
    {
        _recordingLimit?.Cancel();
        var samples = recorder.Stop();
        var target = _target!;
        if (samples.Length < MinSamples)
        {
            State = DictationState.Idle;
            ShowBriefly("Too short; nothing was typed.");
            return;
        }
        State = DictationState.Transcribing;
        SetStatus("Turning speech into text…");
        var current = settings.Load();
        var path = models.PathOf(SpeechModel.Find(current.DictationModel));
        if (_engine == null || _enginePath != path)
        {
            (_engine as IDisposable)?.Dispose();
            _engine = speechToText(path);
            _enginePath = path;
        }
        var watch = Stopwatch.StartNew();
        string text;
        try
        {
            var engine = _engine;
            text = DictationText.Clean(await Task.Run(() => engine.TranscribeAsync(samples, current.DictationLanguage)));
        }
        catch (Exception ex)
        {
            State = DictationState.Idle;
            SetStatus("");
            await dialogs.ShowMessageAsync("Dictation", $"Speech couldn't be turned into text:\n{ex.Message}");
            return;
        }
        var audio = (double)samples.Length / SpeechModel.SampleRate;
        log?.Invoke($"dictation: {audio:0.0} s of audio -> {watch.Elapsed.TotalSeconds:0.00} s, " +
                    $"{Environment.WorkingSet / 1_000_000} MB working set, \"{text}\"");
        State = DictationState.Idle;
        if (text.Length == 0)
        {
            ShowBriefly("Didn't catch anything.");
            return;
        }
        TextReady?.Invoke(target, text);
        ShowBriefly($"Typed into {_targetName}.");
    }

    private void SetStatus(string text)
    {
        _statusVersion++;
        Status = text;
    }

    /// <summary>Shows <paramref name="text"/> for a few seconds, unless something else replaces it.</summary>
    private async void ShowBriefly(string text)
    {
        SetStatus(text);
        var version = _statusVersion;
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (version == _statusVersion) Status = "";
    }
}
