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
}
