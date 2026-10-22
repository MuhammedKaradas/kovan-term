using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kovan-settings-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_FileMissing_ReturnsDefaults()
    {
        Assert.Equal(ThemePreference.System, new SettingsStore(FilePath).Load().Theme);
    }

    [Fact]
    public void SaveLoad_KeepsTheme_AsReadableName()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new AppSettings { Theme = ThemePreference.Light });

        Assert.Equal(ThemePreference.Light, store.Load().Theme);
        Assert.Contains("\"light\"", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "theme": "purple" }""")]
    public void Load_UnreadableFile_ReturnsDefaults(string content)
    {
        File.WriteAllText(FilePath, content);
        Assert.Equal(ThemePreference.System, new SettingsStore(FilePath).Load().Theme);
    }

    [Theory]
    [InlineData(ThemePreference.System, ThemePreference.Dark)]
    [InlineData(ThemePreference.Dark, ThemePreference.Light)]
    [InlineData(ThemePreference.Light, ThemePreference.System)]
    public void NextTheme_CyclesSystemDarkLight(ThemePreference current, ThemePreference expected)
    {
        Assert.Equal(expected, MainViewModel.NextTheme(current));
    }
}
