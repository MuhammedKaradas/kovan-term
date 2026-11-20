using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.Tests;

public sealed class CrashReportsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private static readonly CrashReports.Sanitizer Clean = new("muhammet", "DESKTOP-7XYZ");

    public CrashReportsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData(@"Could not find file 'C:\Users\muhammet\work\secret-project\notes.txt'.", "Could not find file '<path>'.")]
    [InlineData(@"Access to the path C:\Program Files\x y\z is denied.", "Access to the path <path>")]
    [InlineData(@"Share \\fileserver\team\plans.docx failed", "Share <path>")]
    [InlineData("No such file: /home/muhammet/app/x.json", "No such file: <path>")]
    [InlineData("Login failed for muhammet on DESKTOP-7XYZ", "Login failed for <user> on <computer>")]
    [InlineData("Mail me at someone@example.com please", "Mail me at <email> please")]
    [InlineData("Index was outside the bounds of the array.", "Index was outside the bounds of the array.")]
    public void Messages_lose_paths_names_and_addresses(string message, string expected) =>
        Assert.Equal(expected, Clean.Clean(message));

    [Fact]
    public void Stack_frames_keep_only_the_file_name_and_line()
    {
        const string stack = @"   at KovanTerm.Views.MainWindow.OnX() in D:\a\kovan-term\src\KovanTerm\Views\MainWindow.xaml.cs:line 42
   at KovanTerm.Services.Foo.Bar() in C:\Users\muhammet\src\Foo Bar\Foo.cs:line 7
   at System.Threading.Tasks.Task.Run()";

        var cleaned = Clean.CleanStack(stack);

        Assert.Contains(@"MainWindow.OnX() in MainWindow.xaml.cs:line 42", cleaned);
        Assert.Contains("Foo.Bar() in Foo.cs:line 7", cleaned);
        Assert.Contains("Task.Run()", cleaned);
        Assert.DoesNotContain("muhammet", cleaned);
        Assert.DoesNotContain(@"\", cleaned);
    }

    private static Exception Thrown()
    {
        try
        {
            try { throw new FileNotFoundException(@"Could not find file 'C:\Users\muhammet\x.txt'."); }
            catch (Exception inner) { throw new InvalidOperationException("Saving failed", inner); }
        }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void A_report_has_what_the_privacy_note_lists_and_nothing_else()
    {
        var report = CrashReports.Build("Dispatcher", Thrown(), "1b2c3d4e-0000-0000-0000-000000000001", new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(3)), Clean);
        using var json = JsonDocument.Parse(CrashReports.ToJson(report));
        var root = json.RootElement;

        Assert.Equal(["schema", "installId", "appVersion", "os", "architecture", "runtime", "time", "source", "exception", "inner"],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("2026-11-21T07:00:00+00:00", root.GetProperty("time").GetString());
        Assert.Equal("System.InvalidOperationException", root.GetProperty("exception").GetProperty("type").GetString());
        Assert.Equal(["type", "message", "stack"], root.GetProperty("exception").EnumerateObject().Select(p => p.Name));
        var inner = Assert.Single(root.GetProperty("inner").EnumerateArray());
        Assert.Equal("Could not find file '<path>'.", inner.GetProperty("message").GetString());
        Assert.DoesNotContain("muhammet", json.RootElement.GetRawText());
    }

    private sealed class FakeServer(Func<int, HttpStatusCode?> answer) : HttpMessageHandler
    {
        public List<string> Received { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Received.Count;
            Received.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return answer(n) is { } code ? new HttpResponseMessage(code) : throw new HttpRequestException("offline");
        }
    }

    private string Queued(string name, DateTime? written = null)
    {
        var path = Path.Combine(_dir, name + ".json");
        File.WriteAllText(path, $$"""{"n":"{{name}}"}""");
        if (written is { } w) File.SetLastWriteTimeUtc(path, w);
        return path;
    }

    [Fact]
    public async Task Sent_and_refused_reports_go_server_errors_and_offline_wait()
    {
        Queued("1"); Queued("2"); Queued("3"); Queued("4");
        // 1 accepted, 2 refused (bad request), 3 a server error: 3 and 4 wait for the next start
        var server = new FakeServer(n => n switch { 0 => HttpStatusCode.Accepted, 1 => HttpStatusCode.BadRequest, _ => HttpStatusCode.ServiceUnavailable });

        var sent = await CrashReports.SendQueuedAsync(new HttpClient(server), "https://example.com/crash", _dir, DateTimeOffset.UtcNow);

        Assert.Equal(1, sent);
        Assert.Equal(3, server.Received.Count);
        Assert.Equal(["3.json", "4.json"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order());

        var offline = new FakeServer(_ => null);
        Assert.Equal(0, await CrashReports.SendQueuedAsync(new HttpClient(offline), "https://example.com/crash", _dir, DateTimeOffset.UtcNow));
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public async Task Old_reports_and_ones_over_the_limit_are_dropped()
    {
        var now = DateTimeOffset.UtcNow;
        Queued("000-old", now.UtcDateTime.AddDays(-40));
        for (var i = 1; i <= CrashReports.MaxQueued + 2; i++) Queued($"{i:000}");
        var server = new FakeServer(_ => HttpStatusCode.OK);

        await CrashReports.SendQueuedAsync(new HttpClient(server), "https://example.com/crash", _dir, now);

        Assert.Equal(CrashReports.MaxQueued, server.Received.Count);
        Assert.DoesNotContain(server.Received, r => r.Contains("old") || r.Contains("\"001\"") || r.Contains("\"002\""));
    }

    [Fact]
    public void Agreeing_makes_an_install_id_and_keeps_it()
    {
        var on = CrashReports.With(new AppSettings(), send: true);
        Assert.True(on.SendCrashReports);
        Assert.True(Guid.TryParse(on.InstallId, out _));

        var off = CrashReports.With(on, send: false);
        Assert.False(off.SendCrashReports);
        Assert.Equal(on.InstallId, CrashReports.With(off, send: true).InstallId);
    }

    [Fact]
    public void Builds_without_an_address_never_send()
    {
        // Test builds aren't given one
        Assert.Null(CrashReports.Endpoint);
        CrashReports.Apply(CrashReports.With(new AppSettings(), send: true));
        Assert.False(CrashReports.Enabled);
    }
}
