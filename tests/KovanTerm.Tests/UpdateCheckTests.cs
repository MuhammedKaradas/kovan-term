using KovanTerm.Services;

namespace KovanTerm.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("0.2.0", "0.1.0", 1)]
    [InlineData("0.1.0", "0.1.0", 0)]
    [InlineData("0.1.0+abc123", "0.1.0", 0)]
    [InlineData("0.10.0", "0.9.9", 1)]
    [InlineData("1.0.0", "0.99.0", 1)]
    [InlineData("0.2.0-beta.1", "0.2.0", -1)]
    [InlineData("0.2.0-beta.2", "0.2.0-beta.1", 1)]
    [InlineData("0.2", "0.1.5", 1)]
    public void Compare_orders_semver(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(UpdateCheck.Compare(a, b)));

    private static string Release(string tag, bool draft = false, bool prerelease = false, string? url = null) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["tag_name"] = tag,
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["html_url"] = url ?? $"https://github.com/{UpdateCheck.Repository}/releases/tag/{tag}",
        });

    [Fact]
    public void A_newer_release_is_offered_with_its_page()
    {
        var update = UpdateCheck.Newer(Release("v0.2.0"), "0.1.0");

        Assert.Equal(new AvailableUpdate("0.2.0", $"https://github.com/{UpdateCheck.Repository}/releases/tag/v0.2.0"), update);
    }

    [Theory]
    [InlineData("v0.1.0", false, false)]
    [InlineData("v0.0.9", false, false)]
    [InlineData("v0.2.0", true, false)]
    [InlineData("v0.2.0", false, true)]
    public void Same_older_draft_or_prerelease_isnt_offered(string tag, bool draft, bool prerelease) =>
        Assert.Null(UpdateCheck.Newer(Release(tag, draft, prerelease), "0.1.0"));

    [Fact]
    public void A_link_outside_the_repository_is_replaced_with_the_releases_page() =>
        Assert.Equal(UpdateCheck.ReleasesPage, UpdateCheck.Newer(Release("v9.0.0", url: "https://example.com/x"), "0.1.0")!.Url);

    [Fact]
    public void An_answer_without_a_tag_is_ignored() =>
        Assert.Null(UpdateCheck.Newer("""{ "message": "Not Found" }""", "0.1.0"));

    [Fact]
    public void Current_version_has_no_commit_suffix()
    {
        Assert.DoesNotContain('+', UpdateCheck.CurrentVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+", UpdateCheck.CurrentVersion);
    }
}
