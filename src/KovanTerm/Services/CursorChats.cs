using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace KovanTerm.Services;

/// <summary>
/// The Cursor CLI (<c>cursor-agent</c>) keeps a node's conversation: an empty chat is created for the node the first
/// time it starts (<c>cursor-agent create-chat</c> prints its id), and every later start resumes it with
/// <c>--resume &lt;id&gt;</c>.
/// </summary>
public static partial class CursorChats
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>A new chat's id; <c>null</c> if Cursor couldn't make one (not signed in, an update notice…).</summary>
    public static async Task<string?> CreateAsync(string cursorPath, string? directory)
    {
        var (file, args) = Path.GetExtension(cursorPath).ToLowerInvariant() is ".cmd" or ".bat"
            ? ("cmd.exe", $"/d /c \"\"{cursorPath}\" create-chat\"")
            : (cursorPath, "create-chat");
        var info = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory is { Length: > 0 } d && Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        Process? process = null;
        try
        {
            process = Process.Start(info)!;
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(Timeout);
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? ParseChatId(output) : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Debug.WriteLine($"cursor-agent create-chat failed: {ex.Message}");
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
            catch (Exception kill) when (kill is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>The id alone on one line; anything else (an update notice, a login prompt) isn't one.</summary>
    public static string? ParseChatId(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines is [var only] && ChatId().IsMatch(only) ? only : null;
    }

    /// <summary>
    /// The arguments that resume <paramref name="chatId"/>; empty if the profile already picks its chat (or lists them).
    /// The id is checked, since it ends up on a command line.
    /// </summary>
    public static string Arguments(string chatId, string profileArguments) =>
        ChoosesChat().IsMatch(profileArguments) || !ChatId().IsMatch(chatId) ? "" : $"--resume {chatId}";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{7,127}$")] private static partial Regex ChatId();
    [GeneratedRegex(@"(^|\s)(--resume|--continue|-c|ls|resume|create-chat)(\s|=|$)")] private static partial Regex ChoosesChat();
}
