using System.IO;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.ViewModels;

namespace KovanTerm.Tests;

/// <summary>The Settings window: shows settings.json and writes each change back.</summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "kovan-test-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void Changes_AreSavedRightAway_KeepingWhatTheWindowDoesntShow()
    {
        var store = new SettingsStore(_file);
        store.Save(new AppSettings { ShowChangesPanel = true, WorktreeSetupCommands = new() { [@"F:\repo"] = "npm install" } });
        var vm = new SettingsViewModel(store);
        var saves = 0;
        vm.Saved += () => saves++;

        vm.NotifyWhenDone = false;
        vm.DictationLanguage = "tr";
        vm.DictationModel = "small";

        var s = store.Load();
        Assert.Equal((false, "tr", "small"), (s.NotifyWhenDone, s.DictationLanguage, s.DictationModel));
        Assert.True(s.ShowChangesPanel);
        Assert.Equal("npm install", s.WorktreeSetupCommands[@"F:\repo"]);
        Assert.Equal(3, saves);
    }

    [Fact]
    public void TheStatusLight_IsOffUntilTurnedOn_AndKeepsWhereItWasPut()
    {
        var store = new SettingsStore(_file);
        store.Save(new AppSettings { StatusLightRightEdge = false, StatusLightTop = 0.8 });
        var vm = new SettingsViewModel(store);
        Assert.False(vm.StatusLight);

        vm.StatusLight = true;

        var s = store.Load();
        Assert.Equal((true, false, 0.8), (s.StatusLight, s.StatusLightRightEdge, s.StatusLightTop));
    }

    [Fact]
    public void Opening_ItSavesNothing()
    {
        var store = new SettingsStore(_file);
        var vm = new SettingsViewModel(store);
        Assert.False(File.Exists(_file));
        Assert.True(vm.NotifyWhenWaiting);
    }

    [Fact]
    public void TheTheme_GoesToTheMainWindow()
    {
        var store = new SettingsStore(_file);
        ThemePreference? applied = null;
        var vm = new SettingsViewModel(store, t => applied = t);

        vm.Theme = ThemePreference.Light;

        Assert.Equal(ThemePreference.Light, applied);
    }

    [Fact]
    public void ALanguageTypedByHand_IsKeptAndShown()
    {
        var store = new SettingsStore(_file);
        store.Save(new AppSettings { DictationLanguage = "nl", DictationModel = "large-v9" });

        var vm = new SettingsViewModel(store);

        Assert.Equal("nl", vm.DictationLanguage);
        Assert.Contains(vm.Languages, l => l.Value == "nl");
        // An unknown model shows as the one used instead
        Assert.Equal("base", vm.DictationModel);
    }
}
