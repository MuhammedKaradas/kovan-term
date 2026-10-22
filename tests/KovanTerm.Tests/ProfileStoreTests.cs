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
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(FilePath, "[ { corrupt");
        Assert.Equal(ProfileStore.Defaults, new ProfileStore(FilePath).Load());
    }
}
