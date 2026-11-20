using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.Tests;

public sealed class UsageStatsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "usage.json");
    private static readonly DateOnly Today = new(2026, 11, 21);

    public UsageStatsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("claude", false, false, false, "claude", "node.claude")]
    [InlineData("qwen", false, false, false, "qwen", "node.qwen")]
    [InlineData(null, false, false, false, "pwsh", "node.shell")]
    [InlineData(null, false, false, false, "wsl-Debian", "node.shell")]
    [InlineData(null, false, false, false, "my-own-tool", "node.other")]
    [InlineData("some-new-agent", false, false, false, "x", "node.other")]
    [InlineData(null, true, false, false, null, "node.note")]
    [InlineData(null, false, true, false, null, "node.preview")]
    [InlineData(null, false, false, true, null, "node.group")]
    public void Nodes_count_under_a_fixed_name(string? agent, bool note, bool preview, bool group, string? profile, string expected)
    {
        Assert.Equal(expected, UsageStats.NodeEvent(agent, note, preview, group, profile));
        Assert.True(UsageStats.Events.ContainsKey(expected));
    }

    [Theory]
    [InlineData("claude-deepseek", true, "profile.deepseek")]
    [InlineData("claude-ollama", true, "profile.ollama")]
    [InlineData("my-profile", false, "profile.custom")]
    [InlineData("codex", true, null)]
    public void Profiles_worth_counting(string id, bool builtIn, string? expected) =>
        Assert.Equal(expected, UsageStats.ProfileEvent(id, builtIn));

    private static Dictionary<string, Dictionary<string, int>> Days() => new()
    {
        ["2026-11-19"] = new() { ["app.start"] = 2, ["node.claude"] = 3 },
        ["2026-11-20"] = new() { ["feature.dictation"] = 1, ["C:\\secret\\file.txt"] = 9, ["node.codex"] = 0 },
        ["2026-11-21"] = new() { ["app.start"] = 1 },
    };

    [Fact]
    public void Body_has_finished_days_and_only_known_events()
    {
        var body = UsageStats.Body(Days(), "id-1", Today)!;
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        Assert.Equal(["schema", "installId", "appVersion", "os", "days"], root.EnumerateObject().Select(p => p.Name));
        var days = root.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(["2026-11-19", "2026-11-20"], days.Select(d => d.GetProperty("date").GetString()));
        Assert.Equal(3, days[0].GetProperty("counts").GetProperty("node.claude").GetInt32());
        // An unknown name and a zero count aren't sent; today isn't finished
        Assert.Equal(["feature.dictation"], days[1].GetProperty("counts").EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("secret", body);
        Assert.Null(UsageStats.Body(new() { ["2026-11-21"] = new() { ["app.start"] = 1 } }, "id-1", Today));
    }

    private sealed class FakeServer(HttpStatusCode? answer) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return answer is { } code ? Task.FromResult(new HttpResponseMessage(code)) : throw new HttpRequestException("offline");
        }
    }

    private void Write(Dictionary<string, Dictionary<string, int>> days) => File.WriteAllText(File_, JsonSerializer.Serialize(days));

    [Theory]
    [InlineData(HttpStatusCode.OK, true, new[] { "2026-11-21" })]
    [InlineData(HttpStatusCode.BadRequest, false, new[] { "2026-11-21" })]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, new[] { "2026-11-19", "2026-11-20", "2026-11-21" })]
    [InlineData(null, false, new[] { "2026-11-19", "2026-11-20", "2026-11-21" })]
    public async Task Sent_days_are_forgotten_and_the_rest_wait(HttpStatusCode? answer, bool sent, string[] left)
    {
        Write(Days());
        var server = new FakeServer(answer);

        Assert.Equal(sent, await UsageStats.SendAsync(new HttpClient(server), "https://example.com/usage", File_, "id-1", Today));

        Assert.Equal(1, server.Requests);
        Assert.Equal(left, UsageStats.Load(File_).Keys.Order());
    }

    [Fact]
    public async Task Nothing_finished_means_no_request()
    {
        Write(new() { ["2026-11-21"] = new() { ["app.start"] = 1 } });
        var server = new FakeServer(HttpStatusCode.OK);

        Assert.False(await UsageStats.SendAsync(new HttpClient(server), "https://example.com/usage", File_, "id-1", Today));
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public void Agreeing_shares_the_install_id_with_crash_reports()
    {
        var crash = CrashReports.With(new AppSettings(), send: true);
        var both = UsageStats.With(crash, send: true);

        Assert.True(both.SendUsageStats);
        Assert.Equal(crash.InstallId, both.InstallId);
        Assert.True(Guid.TryParse(UsageStats.With(new AppSettings(), send: true).InstallId, out _));
    }

    [Fact]
    public void Builds_without_an_address_never_count()
    {
        Assert.Null(UsageStats.Endpoint);
        UsageStats.Apply(UsageStats.With(new AppSettings(), send: true));
        Assert.False(UsageStats.Enabled);
    }

    [Fact]
    public void The_docs_list_every_event()
    {
        var docs = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "usage-stats.md"));
        foreach (var name in UsageStats.Events.Keys) Assert.Contains($"`{name}`", docs);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KovanTerm.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
