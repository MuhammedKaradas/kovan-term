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
    public void Resolve_PathteUzantisizAdi_PathextIleBulur()
    {
        var cmd = Touch("claude.cmd");
        Touch("claude"); // npm'in bash betiği: uzantısız dosya seçilmemeli

        Assert.Equal(cmd, new CommandResolver(_dir, ".EXE;.CMD").Resolve("claude"));
    }

    [Fact]
    public void Resolve_PathextSirasinaUyar()
    {
        var exe = Touch("tool.exe");
        Touch("tool.cmd");

        Assert.Equal(exe, new CommandResolver(_dir, ".EXE;.CMD").Resolve("tool"));
    }

    [Fact]
    public void Resolve_UzantiVerilmisse_OlduguGibiBulur()
    {
        var exe = Touch("app.exe");
        Assert.Equal(exe, new CommandResolver(_dir, ".EXE").Resolve("app.exe"));
    }

    [Fact]
    public void Resolve_TamYol_PathteAramaz()
    {
        var exe = Touch("x.exe");
        Assert.Equal(exe, new CommandResolver("", ".EXE").Resolve(exe));
    }

    [Fact]
    public void Resolve_Bulunamazsa_NullDoner()
    {
        Assert.Null(new CommandResolver(_dir, ".EXE").Resolve("yok"));
        Assert.Null(new CommandResolver(_dir, ".EXE").Resolve(""));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", "", @"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Tools\pwsh.exe", "-NoLogo", @"C:\Tools\pwsh.exe -NoLogo")]
    [InlineData(@"C:\Program Files\App\app.exe", "-x", "\"C:\\Program Files\\App\\app.exe\" -x")]
    [InlineData(@"C:\Program Files\nodejs\claude.cmd", "", "cmd.exe /k \"\"C:\\Program Files\\nodejs\\claude.cmd\"\"")]
    [InlineData(@"C:\npm\codex.CMD", "--full-auto", "cmd.exe /k \"C:\\npm\\codex.CMD --full-auto\"")]
    public void BuildCommandLine_BatchDosyalariniCmdIleSarar(string path, string args, string expected)
    {
        Assert.Equal(expected, CommandResolver.BuildCommandLine(path, args));
    }

    [Theory]
    [InlineData("cmd.exe /k claude", "cmd.exe")]
    [InlineData("powershell.exe", "powershell.exe")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" -x", @"C:\Program Files\App\app.exe")]
    [InlineData("  boşluklu.exe arg", "boşluklu.exe")]
    public void FirstToken_IlkOgeyiDoner(string commandLine, string expected)
    {
        Assert.Equal(expected, CommandResolver.FirstToken(commandLine));
    }
}
