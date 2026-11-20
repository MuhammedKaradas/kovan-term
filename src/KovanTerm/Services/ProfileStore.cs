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
    /// <summary>
    /// Claude Code on DeepSeek's Anthropic-compatible API, as DeepSeek's guide sets it up: still Claude Code, so status,
    /// Allow/Deny, the agent mesh and resuming work as usual. The API key is asked for the first time and kept in
    /// Credential Manager. Claude model names would be mapped too (Opus to the pricier Pro model), so every one is set.
    /// </summary>
    public static TerminalProfile DeepSeek { get; } = new()
    {
        Id = "claude-deepseek",
        Name = "Claude Code (DeepSeek)",
        Executable = "claude",
        Color = "#4D6BFE",
        Environment = new Dictionary<string, string>
        {
            ["ANTHROPIC_BASE_URL"] = "https://api.deepseek.com/anthropic",
            ["ANTHROPIC_AUTH_TOKEN"] = EnvironmentLauncher.SecretPrefix + "deepseek",
            ["ANTHROPIC_MODEL"] = "deepseek-flash[1m]",
            ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = "deepseek-flash[1m]",
            ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = "deepseek-flash[1m]",
            ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = "deepseek-flash",
            ["CLAUDE_CODE_SUBAGENT_MODEL"] = "deepseek-flash",
            ["CLAUDE_CODE_AUTO_COMPACT_WINDOW"] = "786432",
        },
    };

    public static IReadOnlyList<TerminalProfile> Defaults { get; } =
    [
        new() { Id = "powershell", Name = "PowerShell", Executable = "powershell.exe", Arguments = "-NoLogo" },
        new() { Id = "pwsh", Name = "PowerShell 7", Executable = "pwsh.exe", Arguments = "-NoLogo" },
        new() { Id = "cmd", Name = "cmd", Executable = "cmd.exe" },
        new() { Id = "claude", Name = "Claude Code", Executable = "claude", Color = "#D97757" },
        DeepSeek,
        new() { Id = "codex", Name = "Codex", Executable = "codex", Color = "#10A37F" },
        new() { Id = "gemini", Name = "Gemini CLI", Executable = "gemini", Color = "#4285F4" },
        new() { Id = "cursor", Name = "Cursor CLI", Executable = "cursor-agent", Color = "#8A8A8A" },
        // Both report their status over OSC 7501 (OpenCode from version 2 on); Qwen Code also resumes its conversation
        new() { Id = "opencode", Name = "OpenCode", Executable = "opencode", Color = "#FAB283" },
        new() { Id = "qwen", Name = "Qwen Code", Executable = "qwen", Color = "#615CED" },
        new() { Id = "antigravity", Name = "Antigravity", Executable = "agy", Color = "#3B82F6" },
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

    /// <summary>A shell, Claude Code, Codex and Gemini CLI for each installed WSL distro.</summary>
    public static IEnumerable<TerminalProfile> WslDefaults(IEnumerable<string> distros) =>
        distros.SelectMany(d => new TerminalProfile[]
        {
            new() { Id = $"wsl-{d}", Name = $"{d} (WSL)", Executable = "", Wsl = true, Distro = d, Color = "#E95420" },
            new() { Id = $"claude-wsl-{d}", Name = $"Claude Code ({d})", Executable = "claude", Wsl = true, Distro = d, Color = "#D97757" },
            new() { Id = $"codex-wsl-{d}", Name = $"Codex ({d})", Executable = "codex", Wsl = true, Distro = d, Color = "#10A37F" },
            new() { Id = $"gemini-wsl-{d}", Name = $"Gemini CLI ({d})", Executable = "gemini", Wsl = true, Distro = d, Color = "#4285F4" },
        });

    /// <summary>Profiles; only the defaults if the user file can't be read (the app still starts).</summary>
    public IReadOnlyList<TerminalProfile> Load(IEnumerable<string>? wslDistros = null)
    {
        var result = Defaults.Concat(WslDefaults(wslDistros ?? [])).ToList();
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
