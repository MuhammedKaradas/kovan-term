using KovanTerm.Services;

namespace KovanTerm.Tests;

/// <summary>The link under a Ctrl+click in a terminal.</summary>
public sealed class TerminalLinksTests
{
    private const string Line = "  Local:   http://localhost:5173/dashboard?tab=1   ";

    [Theory]
    [InlineData(11, "http://localhost:5173/dashboard?tab=1")]
    [InlineData(30, "http://localhost:5173/dashboard?tab=1")]
    [InlineData(47, "http://localhost:5173/dashboard?tab=1")]
    [InlineData(5, null)]
    [InlineData(50, null)]
    public void TheLinkUnderTheColumn(int column, string? link) => Assert.Equal(link, TerminalLinks.At(Line, column));

    [Theory]
    [InlineData("See https://example.com/docs.", "https://example.com/docs")]
    [InlineData("(https://example.com/a_(b))", "https://example.com/a_(b)")]
    [InlineData("(see https://example.com/a)", "https://example.com/a")]
    [InlineData("\"https://example.com/q?x=1\",", "https://example.com/q?x=1")]
    public void SentencePunctuation_IsntPartOfIt(string line, string link) =>
        Assert.Equal(link, TerminalLinks.At(line, line.IndexOf("http", StringComparison.Ordinal) + 3));

    [Fact]
    public void AWrappedLink_GoesOnInTheNextLine()
    {
        var first = "go to https://example.com/a/very/long";
        Assert.Equal("https://example.com/a/very/long/path?x=1", TerminalLinks.At(first, 10, "/path?x=1 more", width: first.Length));
        // Not wrapped: it ends before the last column
        Assert.Equal("https://example.com/a/very/long", TerminalLinks.At(first + "   ", 10, "/path?x=1", width: first.Length + 3));
    }

    [Theory]
    [InlineData("http://localhost:3000", true)]
    [InlineData("https://example.com", true)]
    [InlineData("file:///C:/notes.txt", false)]
    [InlineData("mailto:me@example.com", false)]
    public void OnlyWebLinks_GoToAPreview(string link, bool web) => Assert.Equal(web, TerminalLinks.IsWeb(link));
}
