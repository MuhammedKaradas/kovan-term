using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public class EnvironmentLauncherTests
{
    private static string Launcher => Path.Combine(AppContext.BaseDirectory, "KovanLaunch.exe");

    [Fact]
    public void Without_variables_the_command_line_is_unchanged()
    {
        Assert.Equal("claude --x", EnvironmentLauncher.Wrap("claude --x", null, wsl: false));
        Assert.Equal("claude --x", EnvironmentLauncher.Wrap("claude --x", new Dictionary<string, string>(), wsl: false));
    }

    [Fact]
    public void Variables_and_secrets_become_launcher_arguments()
    {
        var line = EnvironmentLauncher.Wrap("\"C:\\Program Files\\x\\claude.exe\" --settings \"a b\"",
            new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = "https://api.example.com/anthropic",
                ["ANTHROPIC_AUTH_TOKEN"] = "secret: deepseek",
            }, wsl: true, launcher: @"C:\k\KovanLaunch.exe");

        Assert.Equal(
        [
            @"C:\k\KovanLaunch.exe",
            "--secret", "ANTHROPIC_AUTH_TOKEN=deepseek",
            "--set", "ANTHROPIC_BASE_URL=https://api.example.com/anthropic",
            "--wsl",
            "--run", "\"C:\\Program Files\\x\\claude.exe\" --settings \"a b\"",
        ], Split(line));
        Assert.DoesNotContain("secret:", line);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("a \"quoted\" word")]
    [InlineData(@"C:\path with space\")]
    [InlineData(@"ends\\")]
    [InlineData(@"\\server\share\""x""")]
    public void Quoted_arguments_come_back_unchanged(string arg) =>
        Assert.Equal(["x.exe", arg, "y"], Split($"x.exe {EnvironmentLauncher.QuoteArgument(arg)} y"));

    [Fact]
    public void Node_starts_through_the_launcher_and_keeps_its_saved_command()
    {
        var node = TerminalNodeViewModel.FromState(new NodeState { Id = Guid.NewGuid(), Title = "t", CommandLine = "claude" },
            profile: new TerminalProfile { Id = "p", Name = "p", Executable = "claude", Environment = new Dictionary<string, string> { ["A"] = "1" } });

        Assert.StartsWith(EnvironmentLauncher.QuoteArgument(EnvironmentLauncher.LauncherPath), node.StartupCommandLine);
        Assert.EndsWith("--run claude", node.StartupCommandLine);
        Assert.Equal("claude", node.ToState().CommandLine);
    }

    [Fact]
    public void Profiles_read_environment_from_json()
    {
        var profile = JsonSerializer.Deserialize<TerminalProfile>(
            """{ "id": "ds", "name": "DeepSeek", "executable": "claude", "environment": { "ANTHROPIC_AUTH_TOKEN": "secret:deepseek" } }""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

        Assert.Equal("secret:deepseek", profile.Environment!["ANTHROPIC_AUTH_TOKEN"]);
    }

    [Fact]
    public void Launcher_sets_the_variables_and_returns_the_programs_exit_code()
    {
        var line = EnvironmentLauncher.Wrap("cmd /d /c if \"%KOVAN_TEST%\"==\"a b \\c\" (exit 3) else (exit 4)",
            new Dictionary<string, string> { ["KOVAN_TEST"] = @"a b \c" }, wsl: false, launcher: Launcher);

        Assert.Equal(3, Run(line));
    }

    [Fact]
    public void Launcher_reports_a_program_it_cant_start()
    {
        var line = EnvironmentLauncher.Wrap("kovan-no-such-program.exe", new Dictionary<string, string> { ["A"] = "1" }, wsl: false, launcher: Launcher);

        Assert.Equal(1, Run(line));
    }

    private static int Run(string commandLine)
    {
        var first = Split(commandLine)[0];
        var psi = new ProcessStartInfo(first, commandLine[EnvironmentLauncher.QuoteArgument(first).Length..].TrimStart())
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(30_000);
        return p.ExitCode;
    }

    /// <summary>How Windows programs split their command line.</summary>
    private static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        try
        {
            return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray();
        }
        finally { LocalFree(argv); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
