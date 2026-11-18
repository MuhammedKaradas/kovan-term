using System.IO;
using KovanTerm.Services;

namespace KovanTerm.Tests;

public sealed class WslLauncherTests : IDisposable
{
    private const string Wsl = @"C:\Windows\System32\wsl.exe";

    private readonly string _dir = Directory.CreateTempSubdirectory("kovan-wsl-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private WslLauncher Launcher(string distroList, bool installed = true)
    {
        if (installed) File.WriteAllText(Path.Combine(_dir, "wsl.exe"), "");
        return new WslLauncher(new CommandResolver(_dir, ".EXE"), () => distroList);
    }

    [Fact]
    public void ParseDistroList_SkipsBlankAndDockerEntries()
    {
        var distros = WslLauncher.ParseDistroList("Debian\r\nUbuntu-24.04\r\n\r\ndocker-desktop\r\ndocker-desktop-data\r\n");

        Assert.Equal(["Debian", "Ubuntu-24.04"], distros);
    }

    [Fact]
    public void IsInstalled_ChecksDistroList()
    {
        var wsl = Launcher("Debian\n");

        Assert.True(wsl.IsInstalled("debian"));
        Assert.True(wsl.IsInstalled(null)); // default distro
        Assert.False(wsl.IsInstalled("Arch"));
    }

    [Fact]
    public void WithoutWsl_NothingIsInstalled()
    {
        var wsl = Launcher("Debian\n", installed: false);

        Assert.Null(wsl.WslPath);
        Assert.Empty(wsl.Distros);
        Assert.False(wsl.IsInstalled(null));
    }

    [Fact]
    public void BuildCommandLine_WithoutExecutable_OpensLoginShell()
    {
        Assert.Equal($"{Wsl} -d Debian --cd /srv/app", WslLauncher.BuildCommandLine(Wsl, "Debian", "/srv/app", "", ""));
        Assert.Equal($"{Wsl} --cd ~", WslLauncher.BuildCommandLine(Wsl, null, "~", "", ""));
    }

    [Fact]
    public void BuildCommandLine_RunsCommandInInteractiveLoginShell()
    {
        var line = WslLauncher.BuildCommandLine(Wsl, "Debian", "/home/me/my repo", "claude", "--permission-mode plan");

        Assert.Equal(
            $"{Wsl} -d Debian --cd \"/home/me/my repo\" -- exec \"${{SHELL:-/bin/sh}}\" -lic "
            + "'command -v '\\''claude'\\'' >/dev/null 2>&1"
            + " || echo '\\''Kovan: claude not found on the login shell PATH of this distro.'\\''; "
            + "claude --permission-mode plan; exec \"${SHELL:-/bin/sh}\" -l'",
            line);
    }

    [Fact]
    public void BuildCommandLine_EscapesSingleQuotesInArguments()
    {
        var line = WslLauncher.BuildCommandLine(Wsl, null, "~", "claude", "'fix it'");

        Assert.Contains(@"claude '\''fix it'\''; exec", line);
    }

    [Theory]
    [InlineData("~", true)]
    [InlineData("/home/me", true)]
    [InlineData(@"C:\code", false)]
    [InlineData("~/code", false)] // wsl.exe --cd only expands a bare ~
    public void IsLinuxPath(string path, bool expected) => Assert.Equal(expected, WslLauncher.IsLinuxPath(path));

    [Fact]
    public void UncPath_RoundTrips()
    {
        var unc = WslLauncher.ToUncPath("Debian", "/home/me/code");

        Assert.Equal(@"\\wsl.localhost\Debian\home\me\code", unc);
        Assert.Equal("/home/me/code", WslLauncher.FromUncPath(unc, "Debian"));
    }

    [Theory]
    [InlineData(@"\\wsl$\Debian\srv\", "/srv")]
    [InlineData(@"\\wsl.localhost\debian", "/")]
    [InlineData(@"\\wsl.localhost\Debian-2\srv", null)]
    [InlineData(@"\\wsl.localhost\Ubuntu\srv", null)]
    [InlineData(@"C:\code", null)]
    public void FromUncPath(string path, string? expected) => Assert.Equal(expected, WslLauncher.FromUncPath(path, "Debian"));

    [Fact]
    public void BuildCommandLine_WithSession_RunsCommandInTmux()
    {
        var id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000045");
        var line = WslLauncher.BuildCommandLine(Wsl, "Debian", "~", "claude", "", WslLauncher.SessionName(id));

        Assert.StartsWith($"{Wsl} -d Debian --cd ~ -- exec \"${{SHELL:-/bin/sh}}\" -lic 'if command -v tmux", line);
        // Kovan's own server and no user config; attach if the session exists
        Assert.Contains("exec tmux -L kovan -f /dev/null start-server", line);
        Assert.Contains("new-session -A -s kovan-aaaaaaaa000000000000000000000045 ", line);
        Assert.Contains(@"set -g prefix None \;", line);
        // Without tmux the command still runs
        Assert.EndsWith("; claude; exec \"${SHELL:-/bin/sh}\" -l'", line);
    }

    [Fact]
    public void BuildCommandLine_ShellWithSession_StartsLoginShellInTmux()
    {
        var line = WslLauncher.BuildCommandLine(Wsl, null, "~", "", "", "kovan-x");

        Assert.Contains("new-session -A -s kovan-x ", line);
        Assert.EndsWith("; exec \"${SHELL:-/bin/sh}\" -l'", line);
    }

    [Fact]
    public void TmuxScript_QuotesTheInnerScriptOnce()
    {
        var script = WslLauncher.TmuxScript("kovan-x", "echo 'hi'");

        Assert.Contains(@"new-session -A -s kovan-x 'echo '\''hi'\'''; fi; ", script);
        Assert.EndsWith("; echo 'hi'", script);
    }

    [Fact]
    public void NodeOfSession_ReadsKovanSessionNames()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, WslLauncher.NodeOfSession(WslLauncher.SessionName(id)));
        Assert.Null(WslLauncher.NodeOfSession("kovan-notaguid"));
        Assert.Null(WslLauncher.NodeOfSession("work"));
    }
}
