using System.IO;
using KovanTerm.Services;

namespace KovanTerm.Tests;

public sealed class CommandResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kovan-path-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Resolve_BareNameOnPath_FindsViaPathext()
    {
        var cmd = Touch("claude.cmd");
        Touch("claude"); // npm's bash script: the extensionless file must not be picked

        Assert.Equal(cmd, new CommandResolver(_dir, ".EXE;.CMD").Resolve("claude"));
    }

    [Fact]
    public void Resolve_FollowsPathextOrder()
    {
        var exe = Touch("tool.exe");
        Touch("tool.cmd");

        Assert.Equal(exe, new CommandResolver(_dir, ".EXE;.CMD").Resolve("tool"));
    }

    [Fact]
    public void Resolve_WithExtension_FindsAsIs()
    {
        var exe = Touch("app.exe");
        Assert.Equal(exe, new CommandResolver(_dir, ".EXE").Resolve("app.exe"));
    }

    [Fact]
    public void Resolve_FullPath_DoesNotSearchPath()
    {
        var exe = Touch("x.exe");
        Assert.Equal(exe, new CommandResolver("", ".EXE").Resolve(exe));
    }

    [Fact]
    public void Resolve_NotFound_ReturnsNull()
    {
        Assert.Null(new CommandResolver(_dir, ".EXE").Resolve("missing"));
        Assert.Null(new CommandResolver(_dir, ".EXE").Resolve(""));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", "", @"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Tools\pwsh.exe", "-NoLogo", @"C:\Tools\pwsh.exe -NoLogo")]
    [InlineData(@"C:\Program Files\App\app.exe", "-x", "\"C:\\Program Files\\App\\app.exe\" -x")]
    [InlineData(@"C:\Program Files\nodejs\claude.cmd", "", "cmd.exe /k \"\"C:\\Program Files\\nodejs\\claude.cmd\"\"")]
    [InlineData(@"C:\npm\codex.CMD", "--full-auto", "cmd.exe /k \"C:\\npm\\codex.CMD --full-auto\"")]
    public void BuildCommandLine_WrapsBatchFilesInCmd(string path, string args, string expected)
    {
        Assert.Equal(expected, CommandResolver.BuildCommandLine(path, args));
    }

    [Theory]
    [InlineData("cmd.exe /k claude", "cmd.exe")]
    [InlineData("powershell.exe", "powershell.exe")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" -x", @"C:\Program Files\App\app.exe")]
    [InlineData("  padded.exe arg", "padded.exe")]
    public void FirstToken_ReturnsFirstToken(string commandLine, string expected)
    {
        Assert.Equal(expected, CommandResolver.FirstToken(commandLine));
    }
}
