using System.Text;
using Whisper.net;

namespace KovanTerm.Services.Dictation;

/// <summary>
/// Local speech-to-text with Whisper (whisper.cpp through Whisper.net). The model is loaded on first use and unloaded
/// after <see cref="UnloadAfter"/> without dictation, so it costs no memory while nobody's dictating.
/// </summary>
public sealed class WhisperSpeechToText(string modelPath) : ISpeechToText, IDisposable
{
    public static readonly TimeSpan UnloadAfter = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private WhisperFactory? _factory;
    private Timer? _unloadTimer;

    public async Task<string> TranscribeAsync(float[] samples, string language, string? prompt = null, CancellationToken cancellation = default)
    {
        await _lock.WaitAsync(cancellation);
        try
        {
            _unloadTimer?.Dispose();
            _factory ??= WhisperFactory.FromPath(modelPath);
            var builder = _factory.CreateBuilder()
                .WithLanguage(string.IsNullOrWhiteSpace(language) ? "auto" : language)
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2));
            if (!string.IsNullOrWhiteSpace(prompt)) builder = builder.WithPrompt(prompt);
            await using var processor = builder.Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellation))
                text.Append(segment.Text);
            return text.ToString();
        }
        finally
        {
            _unloadTimer = new Timer(_ => Unload(), null, UnloadAfter, Timeout.InfiniteTimeSpan);
            _lock.Release();
        }
    }

    private void Unload()
    {
        if (!_lock.Wait(0)) return;
        try
        {
            _factory?.Dispose();
            _factory = null;
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _unloadTimer?.Dispose();
        _factory?.Dispose();
        _factory = null;
    }
}
