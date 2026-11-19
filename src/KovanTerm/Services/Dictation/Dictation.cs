using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace KovanTerm.Services.Dictation;

/// <summary>Records the microphone as 16 kHz mono samples (what Whisper takes).</summary>
public interface IAudioRecorder
{
    /// <summary>Starts recording; throws <see cref="InvalidOperationException"/> if there's no usable microphone.</summary>
    void Start();

    /// <summary>Stops and returns what was recorded, as samples in [-1, 1].</summary>
    float[] Stop();
}

/// <summary>Turns speech into text.</summary>
public interface ISpeechToText
{
    /// <param name="language">A language code such as <c>tr</c> or <c>en</c>, or <c>auto</c>.</param>
    Task<string> TranscribeAsync(float[] samples, string language, CancellationToken cancellation = default);
}

/// <summary>A Whisper model in whisper.cpp's ggml format.</summary>
/// <param name="Size">Download size in bytes.</param>
public sealed record SpeechModel(string Name, long Size)
{
    public const int SampleRate = 16000;

    public string FileName => $"ggml-{Name}.bin";

    public string Url => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}";

    public string SizeText => $"{Size / 1_000_000} MB";

    /// <summary>Multilingual models (the <c>.en</c> ones are English only). Larger ones are more accurate and slower.</summary>
    public static readonly IReadOnlyList<SpeechModel> All =
    [
        new("tiny", 77_691_713),
        new("base", 147_951_465),
        new("small", 487_601_967),
    ];

    public const string DefaultName = "base";

    public static SpeechModel Find(string? name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? All.First(m => m.Name == DefaultName);
}

/// <summary>Where speech models are kept, and downloading them.</summary>
public interface ISpeechModelStore
{
    string PathOf(SpeechModel model);

    bool IsDownloaded(SpeechModel model);

    /// <param name="progress">0–1.</param>
    Task DownloadAsync(SpeechModel model, IProgress<double> progress, CancellationToken cancellation = default);
}

/// <summary>Models in <c>&lt;data folder&gt;\models</c>, downloaded from the whisper.cpp repository on Hugging Face.</summary>
public sealed class SpeechModelStore(string? directory = null, HttpClient? http = null) : ISpeechModelStore
{
    private readonly string _directory = directory ?? Path.Combine(AppPaths.DataDirectory, "models");

    public string PathOf(SpeechModel model) => Path.Combine(_directory, model.FileName);

    public bool IsDownloaded(SpeechModel model) => File.Exists(PathOf(model));

    public async Task DownloadAsync(SpeechModel model, IProgress<double> progress, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(_directory);
        var target = PathOf(model);
        // Into a temporary file first: a half-downloaded model must not look downloaded
        var part = target + ".part";
        var client = http ?? new HttpClient();
        try
        {
            using var response = await client.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? model.Size;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var file = File.Create(part))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    progress.Report(Math.Min(1, (double)done / total));
                }
            }
            File.Move(part, target, overwrite: true);
        }
        finally
        {
            if (http == null) client.Dispose();
            try { File.Delete(part); } catch (IOException) { }
        }
    }
}

/// <summary>Cleans up what Whisper heard before it's typed.</summary>
public static partial class DictationText
{
    /// <summary>
    /// Drops non-speech annotations (<c>[BLANK_AUDIO]</c>, <c>(music)</c>, <c>*coughs*</c>) and joins the segments into
    /// one line: a newline would send the command.
    /// </summary>
    public static string Clean(string text)
    {
        text = NonSpeech().Replace(text, " ");
        return Spaces().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex NonSpeech();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
