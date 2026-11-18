using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class PreviewTests
{
    [Theory]
    [InlineData("  ➜  Local:   http://localhost:5173/\n  ➜  Network: use --host", "http://localhost:5173/")]
    [InlineData("Serving HTTP on 127.0.0.1 port 8765 (http://127.0.0.1:8765/) ...", "http://127.0.0.1:8765/")]
    [InlineData("ready - started server on 0.0.0.0:3000, url: http://0.0.0.0:3000", "http://localhost:3000")]
    [InlineData("Now listening on: https://localhost:7043.", "https://localhost:7043")]
    [InlineData("\x1b[36mhttp://localhost:4200\x1b[39m/", "http://localhost:4200")]
    [InlineData("first http://localhost:1111 then http://[::1]:2222/app", "http://[::1]:2222/app")]
    public void LocalUrls_AreFoundInDevServerOutput(string output, string expected) =>
        Assert.Equal(expected, LocalUrls.FindLast(output));

    [Theory]
    [InlineData("Visit https://github.com/acme/shop for docs")] // only loopback addresses
    [InlineData("compiled in 1.2s")]
    [InlineData("http://localhostile.example.com")]
    public void LocalUrls_IgnoreEverythingElse(string output) => Assert.Null(LocalUrls.FindLast(output));

    [Theory]
    [InlineData("localhost:3000", "http://localhost:3000/")]
    [InlineData(" https://example.com/a ", "https://example.com/a")]
    [InlineData("file:///C:/secret.txt", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    public void AddressBar_AcceptsOnlyWebAddresses(string text, string? expected) =>
        Assert.Equal(expected, LocalUrls.Normalize(text)?.AbsoluteUri);

    [Fact]
    public void PreviewNode_IsSavedAndRestoredAsAPreview()
    {
        var node = new TerminalNodeViewModel { IsBrowser = true, Title = "Preview", CommandLine = "", Url = "http://localhost:5173/" };
        var state = node.ToState();
        Assert.Equal("http://localhost:5173/", state.Url);
        var restored = TerminalNodeViewModel.FromState(state);
        Assert.True(restored.IsBrowser);
        Assert.Equal("http://localhost:5173/", restored.Url);
        Assert.Null(restored.AgentKind is { Length: > 0 } ? restored.AgentKind : null); // not an agent: no mesh, no screen rules

        // A terminal's offered preview address isn't saved
        var terminal = new TerminalNodeViewModel { Title = "dev", CommandLine = "cmd.exe", PreviewUrl = "http://localhost:3000" };
        Assert.Null(terminal.ToState().Url);
        Assert.False(TerminalNodeViewModel.FromState(terminal.ToState()).IsBrowser);
    }
}
