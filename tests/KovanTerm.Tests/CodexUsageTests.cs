using System.IO;
using KovanTerm.Services;

namespace KovanTerm.Tests;

public sealed class CodexUsageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    public CodexUsageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static long Unix(DateTimeOffset t) => t.ToUnixTimeSeconds();

    private static string Line(double primary, int primaryMinutes, DateTimeOffset primaryReset, string secondary = "null", string plan = "plus") =>
        """{"timestamp":"2026-11-21T11:00:00Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"limit_id":"codex","primary":{"used_percent":"""
        + primary.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ""","window_minutes":""" + primaryMinutes + ""","resets_at":""" + Unix(primaryReset)
        + """},"secondary":""" + secondary + ""","credits":{"has_credits":false},"plan_type":""" + "\"" + plan + "\"" + "}}}";

    [Fact]
    public void The_latest_record_is_found_even_with_nested_objects_and_strings()
    {
        var text = Line(10, 300, Now.AddHours(1)) + "\n" + """{"x":"text with \"rate_limits\":{ inside"}""" + "\n" + Line(42, 300, Now.AddHours(2));

        var json = CodexUsage.Latest(text)!;

        Assert.Contains("42", json);
        Assert.EndsWith("\"plus\"}", json);
    }

    [Fact]
    public void Windows_become_rows_with_their_labels_and_reset_times()
    {
        var secondary = $$"""{"used_percent":71.5,"window_minutes":10080,"resets_at":{{Unix(Now.AddDays(3))}}}""";
        var (limits, plan) = CodexUsage.Parse(CodexUsage.Latest(Line(26, 300, Now.AddHours(2), secondary))!, Now);

        Assert.Equal("plus", plan);
        Assert.Equal(["5h", "Week"], limits.Select(l => l.Label));
        Assert.Equal([26, 71.5], limits.Select(l => l.Percent));
        Assert.Equal(Now.AddHours(2), limits[0].ResetsAt);
    }

    [Fact]
    public void A_window_that_has_reset_since_shows_zero()
    {
        var (limits, _) = CodexUsage.Parse(CodexUsage.Latest(Line(95, 43200, Now.AddMinutes(-5), plan: "free"))!, Now);

        var l = Assert.Single(limits);
        Assert.Equal("Month", l.Label);
        Assert.Equal(0, l.Percent);
        Assert.Null(l.ResetsAt);
    }

    [Theory]
    [InlineData(300, "5h")]
    [InlineData(60, "1h")]
    [InlineData(10080, "Week")]
    [InlineData(43200, "Month")]
    [InlineData(4320, "3d")]
    [InlineData(0, "Limit")]
    public void Window_labels(int minutes, string label) => Assert.Equal(label, CodexUsage.WindowLabel(minutes, "primary"));

    [Fact]
    public void The_newest_session_is_read_and_nothing_means_nothing()
    {
        Assert.Empty(new CodexUsage(_dir).Read(Now).Limits);

        var older = Directory.CreateDirectory(Path.Combine(_dir, "2026", "11", "20")).FullName;
        var newer = Directory.CreateDirectory(Path.Combine(_dir, "2026", "11", "21")).FullName;
        File.WriteAllText(Path.Combine(older, "a.jsonl"), Line(5, 300, Now.AddHours(1)) + "\n");
        File.WriteAllText(Path.Combine(newer, "b.jsonl"), Line(33, 300, Now.AddHours(1)) + "\n");
        // The newest file has no limits yet (a session just started): the one before it is used
        File.WriteAllText(Path.Combine(newer, "c.jsonl"), """{"type":"session_meta"}""" + "\n");
        File.SetLastWriteTimeUtc(Path.Combine(newer, "c.jsonl"), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(33, Assert.Single(new CodexUsage(_dir).Read(Now).Limits).Percent);
    }
}
