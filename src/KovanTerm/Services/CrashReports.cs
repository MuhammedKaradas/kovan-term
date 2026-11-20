using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>
/// Crash reports, sent only when the user agreed (Settings → Privacy) and only by builds that were given an address
/// (<c>-p:KovanCrashReportEndpoint=https://…</c>). A crash writes one report to the queue folder; the next start sends
/// what's queued, and keeps what couldn't be sent for later. The format is in <c>docs/crash-reports.md</c>.
/// </summary>
public static partial class CrashReports
{
    public const int Schema = 1;
    /// <summary>At most this many reports wait to be sent; older ones are dropped.</summary>
    public const int MaxQueued = 20;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private const int MaxMessageLength = 2000;
    private const int MaxStackLength = 16_000;

    /// <summary>Where reports go; <c>null</c> in builds without one (source builds), which never ask or send.</summary>
    public static string? Endpoint { get; } = ReadEndpoint();

    private static string? ReadEndpoint()
    {
        var value = typeof(CrashReports).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "CrashReportEndpoint")?.Value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    }

    public static string QueueDirectory => Path.Combine(AppPaths.DataDirectory, "crash-reports");

    /// <summary>Set at startup and when the setting changes: a crash can't read settings safely.</summary>
    public static volatile bool Enabled;
    public static string? InstallId;

    /// <summary>Takes the user's choice from the settings.</summary>
    public static void Apply(Models.AppSettings settings)
    {
        InstallId = settings.InstallId;
        Enabled = Endpoint != null && settings.SendCrashReports == true && settings.InstallId != null;
    }

    /// <summary>The settings with reports turned on or off; turning them on makes the install id if there's none.</summary>
    public static Models.AppSettings With(Models.AppSettings settings, bool send) => settings with
    {
        SendCrashReports = send,
        InstallId = send ? settings.InstallId ?? Guid.NewGuid().ToString() : settings.InstallId,
    };

    // ---- The report ----

    public sealed record ExceptionInfo(string Type, string Message, string Stack);

    public sealed record Report(
        int Schema, string InstallId, string AppVersion, string Os, string Architecture, string Runtime,
        DateTimeOffset Time, string Source, ExceptionInfo Exception, IReadOnlyList<ExceptionInfo> Inner);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string ToJson(Report report) => JsonSerializer.Serialize(report, Json);

    /// <summary>The report for <paramref name="ex"/>, with paths and names that could identify the user removed.</summary>
    public static Report Build(string source, Exception ex, string installId, DateTimeOffset time, Sanitizer? sanitizer = null)
    {
        var s = sanitizer ?? Sanitizer.ForThisComputer();
        var inner = new List<ExceptionInfo>();
        // AggregateException's inner exceptions and the InnerException chain, a few levels deep
        var pending = new Queue<Exception>(ex is AggregateException agg ? agg.InnerExceptions : ex.InnerException is { } i ? [i] : []);
        while (pending.Count > 0 && inner.Count < 5)
        {
            var e = pending.Dequeue();
            inner.Add(Info(e, s));
            if (e.InnerException is { } next) pending.Enqueue(next);
        }
        return new Report(Schema, installId, UpdateCheck.CurrentVersion,
            $"Windows {Environment.OSVersion.Version}", RuntimeInformation.OSArchitecture.ToString(), Environment.Version.ToString(),
            time.ToUniversalTime(), source, Info(ex, s), inner);
    }

    private static ExceptionInfo Info(Exception e, Sanitizer s) => new(
        e.GetType().FullName ?? e.GetType().Name,
        Cap(s.Clean(e.Message), MaxMessageLength),
        Cap(s.CleanStack(e.StackTrace ?? ""), MaxStackLength));

    private static string Cap(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>
    /// Removes what could identify the user or their work: file paths (stack frames keep only the file name and line),
    /// the user and computer names, and e-mail addresses.
    /// </summary>
    public sealed partial class Sanitizer(string? userName, string? machineName)
    {
        public static Sanitizer ForThisComputer() => new(Environment.UserName, Environment.MachineName);

        public string Clean(string text)
        {
            text = FramePath().Replace(text, m => $" in {Path.GetFileName(m.Groups[1].Value.TrimEnd())}:line ");
            text = WindowsPath().Replace(text, "<path>");
            text = LinuxPath().Replace(text, "<path>");
            text = Email().Replace(text, "<email>");
            if (userName is { Length: > 1 }) text = Regex.Replace(text, $@"\b{Regex.Escape(userName)}\b", "<user>", RegexOptions.IgnoreCase);
            if (machineName is { Length: > 1 }) text = Regex.Replace(text, $@"\b{Regex.Escape(machineName)}\b", "<computer>", RegexOptions.IgnoreCase);
            return text;
        }

        public string CleanStack(string stack) => Clean(stack);

        // " in C:\src\x\File.cs:line 12": the file name and line say where, the folder says nothing more useful
        [GeneratedRegex(@" in ((?:[A-Za-z]:\\|\\\\|/)[^:\r\n]*?):line ")] private static partial Regex FramePath();
        // C:\… or \\server\…, up to a quote or the line's end: a path may hold spaces, so nothing after it on the line is kept
        [GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^""'<>|\r\n]*")] private static partial Regex WindowsPath();
        [GeneratedRegex(@"(?<![\w.])/(?:home|mnt|Users|root|tmp|var|opt|etc|usr|srv|wsl\.localhost)/[^\s""'<>|]*")] private static partial Regex LinuxPath();
        [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")] private static partial Regex Email();
    }

    // ---- Queue ----

    /// <summary>Writes the report for a crash, if the user agreed; called from the crash handler, so it never throws.</summary>
    public static void Queue(string source, Exception? ex)
    {
        if (!Enabled || Endpoint == null || ex == null || InstallId is not { } id) return;
        try
        {
            Directory.CreateDirectory(QueueDirectory);
            var now = DateTimeOffset.UtcNow;
            File.WriteAllText(Path.Combine(QueueDirectory, $"{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"),
                ToJson(Build(source, ex, id, now)), Encoding.UTF8);
        }
        catch (Exception) { /* a crash report must never cause another crash */ }
    }

    /// <summary>
    /// Sends the queued reports, oldest first. Sent ones and ones the server refuses (4xx) are deleted; the rest wait for
    /// the next start. Old reports and ones over <see cref="MaxQueued"/> are dropped. Returns how many were sent.
    /// </summary>
    public static async Task<int> SendQueuedAsync(HttpClient http, string endpoint, string directory, DateTimeOffset now)
    {
        if (!Directory.Exists(directory)) return 0;
        var files = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToList();
        foreach (var old in files.Where(f => now - File.GetLastWriteTimeUtc(f) > MaxAge).Concat(files.SkipLast(MaxQueued)).Distinct().ToList())
        {
            TryDelete(old);
            files.Remove(old);
        }
        var sent = 0;
        foreach (var file in files)
        {
            string body;
            try { body = await File.ReadAllTextAsync(file); }
            catch (IOException) { continue; }
            HttpResponseMessage response;
            try
            {
                response = await http.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                break; // offline: try again next time
            }
            using (response)
            {
                var code = (int)response.StatusCode;
                if (response.IsSuccessStatusCode) { TryDelete(file); sent++; }
                else if (code is >= 400 and < 500 && response.StatusCode != HttpStatusCode.TooManyRequests) TryDelete(file);
                else break; // the server is having trouble: later
            }
        }
        return sent;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"KovanTerm/{UpdateCheck.CurrentVersion}");
        return http;
    });

    /// <summary>Sends what's queued, if the user agreed; on a background thread, failures ignored.</summary>
    public static Task SendQueuedAsync() =>
        !Enabled || Endpoint is not { } endpoint ? Task.CompletedTask
        : Task.Run(async () =>
        {
            try { await SendQueuedAsync(SharedHttp.Value, endpoint, QueueDirectory, DateTimeOffset.UtcNow); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException) { }
        });
}
