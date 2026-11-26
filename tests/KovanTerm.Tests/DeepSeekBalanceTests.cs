using System.Net;
using System.Net.Http;
using KovanTerm.Models;
using KovanTerm.Services;

namespace KovanTerm.Tests;

/// <summary>The DeepSeek balance in the limits panel (#132).</summary>
public sealed class DeepSeekBalanceTests
{
    private const string Answer = """
        {"is_available": true, "balance_infos": [
          {"currency": "USD", "total_balance": "4.25", "granted_balance": "0.25", "topped_up_balance": "4.00"}]}
        """;

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
    public void Reads_the_balance()
    {
        var account = DeepSeekBalance.Parse(Answer)!;
        Assert.Equal("$4.25", account.Text);
        Assert.False(account.IsLow);
        Assert.Contains("topped up 4.00", account.Details);

        var two = DeepSeekBalance.Parse("""{"is_available": false, "balance_infos": [{"currency": "CNY", "total_balance": "0.50"}, {"currency": "USD", "total_balance": 0}]}""")!;
        Assert.Equal("¥0.50 · $0.00", two.Text);
        Assert.True(two.IsLow);
        Assert.Contains("Not enough", two.Details);

        Assert.Null(DeepSeekBalance.Parse("<html>"));
        Assert.Null(DeepSeekBalance.Parse("""{"error": "x"}"""));
    }

    [Fact]
    public void Finds_the_key_of_a_profile_on_deepseek()
    {
        Assert.Equal("deepseek", DeepSeekBalance.KeyName([ProfileStore.DeepSeek]));
        var copy = new TerminalProfile
        {
            Id = "mine", Name = "Mine", Executable = "claude",
            Environment = new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "https://API.deepseek.com/anthropic", ["ANTHROPIC_API_KEY"] = "secret: work " },
        };
        Assert.Equal("work", DeepSeekBalance.KeyName([copy]));
        var plain = new TerminalProfile
        {
            Id = "other", Name = "Other", Executable = "claude",
            Environment = new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "https://example.com", ["ANTHROPIC_AUTH_TOKEN"] = "secret:x" },
        };
        Assert.Null(DeepSeekBalance.KeyName([plain, new TerminalProfile { Id = "c", Name = "C", Executable = "claude" }]));
    }

    [Fact]
    public async Task Asks_deepseek_only_with_a_saved_key()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Answer);
        Assert.Null(await new DeepSeekBalance(new HttpClient(handler), _ => null).ReadAsync("deepseek"));
        Assert.Null(handler.Request);

        var account = await new DeepSeekBalance(new HttpClient(handler), k => k == "deepseek" ? "sk-test" : null).ReadAsync("deepseek");
        Assert.Equal("$4.25", account!.Text);
        Assert.Equal("api.deepseek.com", handler.Request!.RequestUri!.Host);
        Assert.Equal("sk-test", handler.Request.Headers.Authorization!.Parameter);

        Assert.Null(await new DeepSeekBalance(new HttpClient(new FakeHandler(HttpStatusCode.Unauthorized, "")), _ => "sk").ReadAsync("deepseek"));
    }
}
