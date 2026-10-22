using System.Diagnostics;
using System.IO;
using System.Text.Json;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>
/// Default profiles + the profiles in the user's <c>%APPDATA%\KovanTerm\profiles.json</c> file.
/// A user profile with the same <see cref="TerminalProfile.Id"/> overrides the default.
/// </summary>
public sealed class ProfileStore
{
    public static IReadOnlyList<TerminalProfile> Defaults { get; } =
    [
        new() { Id = "powershell", Name = "PowerShell", Executable = "powershell.exe", Arguments = "-NoLogo" },
        new() { Id = "pwsh", Name = "PowerShell 7", Executable = "pwsh.exe", Arguments = "-NoLogo" },
        new() { Id = "cmd", Name = "cmd", Executable = "cmd.exe" },
        new() { Id = "claude", Name = "Claude Code", Executable = "claude", Color = "#D97757" },
        new() { Id = "codex", Name = "Codex", Executable = "codex", Color = "#10A37F" },
        new() { Id = "gemini", Name = "Gemini CLI", Executable = "gemini", Color = "#4285F4" },
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string FilePath { get; }

    public ProfileStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "profiles.json");
    }

    /// <summary>Profiles; only the defaults if the user file can't be read (the app still starts).</summary>
    public IReadOnlyList<TerminalProfile> Load()
    {
        var result = Defaults.ToList();
        foreach (var user in LoadUserProfiles())
        {
            var i = result.FindIndex(p => p.Id == user.Id);
            if (i >= 0) result[i] = user;
            else result.Add(user);
        }
        return result;
    }

    private List<TerminalProfile> LoadUserProfiles()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<TerminalProfile>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            Debug.WriteLine($"Couldn't read profiles.json, using defaults only: {ex.Message}");
            return [];
        }
    }
}
