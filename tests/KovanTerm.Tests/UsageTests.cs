using System.IO;
using System.Net;
using System.Net.Http;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class UsageTests : IDisposable
{
    private readonly string _credentials = Path.Combine(Path.GetTempPath(), $"kovan-creds-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_credentials);

    private static string Creds(string token, long expiresAt) =>
        "{\"claudeAiOauth\":{\"accessToken\":\"" + token + "\",\"expiresAt\":" + expiresAt + "}}";

    // The shape of a real response (other fields left out)
    private const string Response = """
        {
          "five_hour": { "utilization": 26.0, "resets_at": "2026-11-17T03:10:00.235592+00:00" },
          "limits": [
            { "kind": "session", "group": "session", "percent": 26, "severity": "normal", "resets_at": "2026-11-17T03:10:00.235592+00:00", "scope": null },
            { "kind": "weekly_all", "group": "weekly", "percent": 46, "severity": "normal", "resets_at": "2026-11-17T00:00:00.235616+00:00", "scope": null },
            { "kind": "weekly_scoped", "group": "weekly", "percent": 6, "severity": "normal", "resets_at": "2026-11-17T00:00:00+00:00",
              "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null } },
            { "kind": "monthly_new_thing", "percent": 91, "severity": "warning" },
            { "kind": "broken" }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsLimits()
    {
        var limits = ClaudeUsage.Parse(Response);
        Assert.Equal(["5h", "Week", "Week · Fable", "monthly new thing"], limits.Select(l => l.Label));
        Assert.Equal(26, limits[0].Percent);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 3, 10, 0, 235, TimeSpan.Zero).AddTicks(5920), limits[0].ResetsAt);
        Assert.False(limits[1].IsHigh);
        Assert.True(limits[3].IsHigh);
        Assert.Null(limits[3].ResetsAt);
        Assert.Empty(ClaudeUsage.Parse("""{"something":"else"}"""));
    }

    [Fact]
    public void Rows_SayWhenTheyReset()
    {
        var now = new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);
        var limits = ClaudeUsage.Parse(Response);
        Assert.Equal("26 % · resets in 2 h 10 m", new UsageRow(limits[0], now).Text);
        Assert.Equal("46 % · resets now", new UsageRow(limits[1], now.AddHours(1)).Text);
        Assert.Equal("91 %", new UsageRow(limits[3], now).Text);
        Assert.Equal("in 3 d", new UsageLimit("Week", 1, now.AddDays(3).AddHours(2), null).ResetsIn(now));
        Assert.Equal("in 5 m", new UsageLimit("5h", 1, now.AddMinutes(5.5), null).ResetsIn(now));
    }

    [Fact]
    public void Token_OnlyWhenLoggedInAndNotExpired()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(ClaudeUsage.ReadToken(_credentials, now)); // no file
        File.WriteAllText(_credentials, Creds("tok", now.AddHours(1).ToUnixTimeMilliseconds()));
        Assert.Equal("tok", ClaudeUsage.ReadToken(_credentials, now));
        File.WriteAllText(_credentials, Creds("tok", now.AddMinutes(-1).ToUnixTimeMilliseconds()));
        Assert.Null(ClaudeUsage.ReadToken(_credentials, now)); // expired: not refreshed here
        File.WriteAllText(_credentials, "not json");
        Assert.Null(ClaudeUsage.ReadToken(_credentials, now));
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task ReadAsync_SendsTheTokenToAnthropicOnly_AndHidesFailures()
    {
        File.WriteAllText(_credentials, Creds("secret", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));
        var ok = new FakeHandler(HttpStatusCode.OK, Response);
        Assert.Equal(4, (await new ClaudeUsage(new HttpClient(ok), _credentials).ReadAsync()).Count);
        Assert.Equal("api.anthropic.com", ok.Request!.RequestUri!.Host);
        Assert.Equal("https", ok.Request.RequestUri.Scheme);
        Assert.Equal("Bearer secret", ok.Request.Headers.Authorization!.ToString());

        Assert.Empty(await new ClaudeUsage(new HttpClient(new FakeHandler(HttpStatusCode.Unauthorized, "")), _credentials).ReadAsync());
        Assert.Empty(await new ClaudeUsage(new HttpClient(new FakeHandler(HttpStatusCode.OK, "<html>")), _credentials).ReadAsync());
        Assert.Empty(await new ClaudeUsage(new HttpClient(ok), _credentials + ".missing").ReadAsync());
    }
}
