using System.IO;
using KovanTerm.Services;

namespace KovanTerm.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kovan-profiles-").FullName;
    private string FilePath => Path.Combine(_dir, "profiles.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_FileMissing_ReturnsDefaults()
    {
        Assert.Equal(ProfileStore.Defaults, new ProfileStore(FilePath).Load());
    }

    [Fact]
    public void Load_UserProfiles_AreAddedAndSameIdOverridesDefault()
    {
        File.WriteAllText(FilePath, """
            [
              // comments and trailing commas are supported
              { "id": "claude", "name": "Claude (plan mode)", "executable": "claude", "arguments": "--permission-mode plan" },
              { "id": "aider", "name": "Aider", "executable": "aider", "color": "#14B014" },
            ]
            """);

        var profiles = new ProfileStore(FilePath).Load();

        Assert.Equal(ProfileStore.Defaults.Count + 1, profiles.Count);
        var claude = Assert.Single(profiles, p => p.Id == "claude");
        Assert.Equal("--permission-mode plan", claude.Arguments);
        Assert.Equal("Aider", profiles[^1].Name);
    }

    [Fact]
    public void Load_AddsShellAndAgentsForEachWslDistro()
    {
        var profiles = new ProfileStore(FilePath).Load(["Debian"]);

        var wsl = profiles.Skip(ProfileStore.Defaults.Count).ToList();
        Assert.Equal(["wsl-Debian", "claude-wsl-Debian", "codex-wsl-Debian", "gemini-wsl-Debian"], wsl.Select(p => p.Id));
        Assert.All(wsl, p => Assert.True(p.Wsl && p.Distro == "Debian"));
        Assert.Equal(["", "claude", "codex", "gemini"], wsl.Select(p => p.Executable));
    }

    [Fact]
    public void Load_UserWslProfile_IsRead()
    {
        File.WriteAllText(FilePath, """
            [{ "id": "codex-wsl", "name": "Codex (WSL)", "executable": "codex", "wsl": true, "distro": "Ubuntu" }]
            """);

        var codex = new ProfileStore(FilePath).Load()[^1];

        Assert.True(codex.Wsl);
        Assert.Equal("Ubuntu", codex.Distro);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(FilePath, "[ { corrupt");
        Assert.Equal(ProfileStore.Defaults, new ProfileStore(FilePath).Load());
    }

    [Fact]
    public void DeepSeek_profile_runs_Claude_Code_with_its_key_in_Credential_Manager()
    {
        var p = Assert.Single(ProfileStore.Defaults, d => d.Id == "claude-deepseek");

        Assert.Equal("claude", p.Executable);
        Assert.Equal("https://api.deepseek.com/anthropic", p.Environment!["ANTHROPIC_BASE_URL"]);
        Assert.Equal("secret:deepseek", p.Environment["ANTHROPIC_AUTH_TOKEN"]);
        // Claude Code maps unset Opus names to DeepSeek's pricier Pro model: every model it might ask for is set
        foreach (var name in new[] { "ANTHROPIC_MODEL", "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL", "CLAUDE_CODE_SUBAGENT_MODEL" })
            Assert.StartsWith("deepseek-flash", p.Environment[name]);
    }
}
