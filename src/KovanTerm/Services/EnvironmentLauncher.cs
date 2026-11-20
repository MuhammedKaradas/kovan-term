using System.IO;
using System.Text;

namespace KovanTerm.Services;

/// <summary>
/// A profile's environment variables (<see cref="Models.TerminalProfile.Environment"/>): the terminal runs
/// <c>KovanLaunch.exe</c>, which sets them and starts the real command line. A value <c>secret:&lt;key&gt;</c> is an
/// API key read from Windows Credential Manager by the launcher, so it's never in a file or on a command line.
/// </summary>
public static class EnvironmentLauncher
{
    public const string SecretPrefix = "secret:";

    public static string LauncherPath => Path.Combine(AppContext.BaseDirectory, "KovanLaunch.exe");

    /// <summary>The command line wrapped in the launcher, or unchanged when there's nothing to set.</summary>
    public static string Wrap(string commandLine, IReadOnlyDictionary<string, string>? environment, bool wsl, string? launcher = null)
    {
        var variables = (environment ?? new Dictionary<string, string>())
            .Where(v => v.Key.Length > 0 && !v.Key.Contains('=') && v.Value != null)
            .OrderBy(v => v.Key, StringComparer.Ordinal)
            .ToList();
        if (variables.Count == 0) return commandLine;
        var sb = new StringBuilder(QuoteArgument(launcher ?? LauncherPath));
        foreach (var (name, value) in variables)
        {
            var secret = value.StartsWith(SecretPrefix, StringComparison.Ordinal);
            sb.Append(secret ? " --secret " : " --set ")
                .Append(QuoteArgument($"{name}={(secret ? value[SecretPrefix.Length..].Trim() : value)}"));
        }
        if (wsl) sb.Append(" --wsl");
        return sb.Append(" --run ").Append(QuoteArgument(commandLine)).ToString();
    }

    /// <summary>One argument, quoted so the program's argument parser (CommandLineToArgvW rules) gets it back unchanged.</summary>
    public static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            // Backslashes before a quote are escaped, and the quote too; elsewhere they're literal
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }
        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
