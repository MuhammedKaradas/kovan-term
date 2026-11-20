using CommunityToolkit.Mvvm.ComponentModel;
using KovanTerm.Models;
using KovanTerm.Services;
using KovanTerm.Services.Dictation;

namespace KovanTerm.ViewModels;

/// <summary>A choice in a list: what's stored, and what's shown.</summary>
public sealed record SettingChoice(string Value, string Label);

/// <summary>
/// The Settings window: the preferences that only lived in <c>settings.json</c>, saved as soon as they change. Values
/// that aren't shown here (panels, worktree setup commands) are kept as they are.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly Action<ThemePreference>? _applyTheme;
    private bool _loading;

    public SettingsViewModel(SettingsStore store, Action<ThemePreference>? applyTheme = null)
    {
        _store = store;
        _applyTheme = applyTheme;
        var s = store.Load();
        _loading = true;
        Theme = s.Theme;
        TitlesFromClaude = s.TitlesFromClaude;
        NotifyWhenWaiting = s.NotifyWhenWaiting;
        NotifyWhenDone = s.NotifyWhenDone;
        KeepAwakeWhileWorking = s.KeepAwakeWhileWorking;
        StatusLight = s.StatusLight;
        OpenWebLinksInPreview = s.OpenWebLinksInPreview;
        StartAgentsOnOpen = s.StartAgentsOnOpen;
        CheckForUpdates = s.CheckForUpdates;
        SendCrashReports = s.SendCrashReports == true;
        SendUsageStats = s.SendUsageStats == true;
        DictationModel = SpeechModel.Find(s.DictationModel).Name;
        // A language typed into the file by hand is kept, and shown
        if (Languages.All(l => l.Value != s.DictationLanguage))
            Languages = [.. Languages, new SettingChoice(s.DictationLanguage, s.DictationLanguage)];
        DictationLanguage = s.DictationLanguage;
        _loading = false;
    }

    public IReadOnlyList<ThemePreference> Themes { get; } = [ThemePreference.System, ThemePreference.Dark, ThemePreference.Light];

    public IReadOnlyList<SettingChoice> Models { get; } =
        SpeechModel.All.Select(m => new SettingChoice(m.Name, $"{m.Name} ({m.SizeText}){(m.Name == SpeechModel.DefaultName ? ", recommended" : "")}")).ToList();

    public IReadOnlyList<SettingChoice> Languages { get; private set; } =
    [
        new("auto", "Detect it (Turkish and English commands both work)"),
        new("tr", "Turkish"),
        new("en", "English"),
        new("de", "German"),
        new("fr", "French"),
        new("es", "Spanish"),
        new("it", "Italian"),
        new("pt", "Portuguese"),
        new("ru", "Russian"),
        new("ja", "Japanese"),
        new("zh", "Chinese"),
    ];

    [ObservableProperty] public partial ThemePreference Theme { get; set; }
    [ObservableProperty] public partial bool TitlesFromClaude { get; set; }
    [ObservableProperty] public partial bool NotifyWhenWaiting { get; set; }
    [ObservableProperty] public partial bool NotifyWhenDone { get; set; }
    [ObservableProperty] public partial bool KeepAwakeWhileWorking { get; set; }
    [ObservableProperty] public partial bool StatusLight { get; set; }
    [ObservableProperty] public partial bool OpenWebLinksInPreview { get; set; }
    [ObservableProperty] public partial bool StartAgentsOnOpen { get; set; }
    [ObservableProperty] public partial bool CheckForUpdates { get; set; }
    [ObservableProperty] public partial bool SendCrashReports { get; set; }
    [ObservableProperty] public partial bool SendUsageStats { get; set; }

    /// <summary>Only builds with an address for them show these settings, and the Privacy section if either.</summary>
    public bool CanSendCrashReports => CrashReports.Endpoint != null;
    public bool CanSendUsageStats => UsageStats.Endpoint != null;
    public bool ShowsPrivacy => CanSendCrashReports || CanSendUsageStats;

    /// <summary>The running version, for the About section.</summary>
    public string Version => $"Kovan Term {UpdateCheck.CurrentVersion}";
    public string ReleasesPage => UpdateCheck.ReleasesPage;
    [ObservableProperty] public partial string DictationModel { get; set; } = SpeechModel.DefaultName;
    [ObservableProperty] public partial string DictationLanguage { get; set; } = "auto";

    /// <summary>Where the file is, for those who'd rather edit it.</summary>
    public string FilePath => _store.FilePath;

    partial void OnThemeChanged(ThemePreference value)
    {
        if (_loading) return;
        // The main window owns the theme (it saves it too)
        if (_applyTheme != null) _applyTheme(value);
        else Save();
    }

    partial void OnTitlesFromClaudeChanged(bool value) => Save();
    partial void OnNotifyWhenWaitingChanged(bool value) => Save();
    partial void OnNotifyWhenDoneChanged(bool value) => Save();
    partial void OnKeepAwakeWhileWorkingChanged(bool value) => Save();
    partial void OnStatusLightChanged(bool value) => Save();
    partial void OnOpenWebLinksInPreviewChanged(bool value) => Save();
    partial void OnStartAgentsOnOpenChanged(bool value) => Save();
    partial void OnCheckForUpdatesChanged(bool value) => Save();

    partial void OnSendCrashReportsChanged(bool value)
    {
        if (_loading) return;
        _store.Save(CrashReports.With(_store.Load(), value));
        CrashReports.Apply(_store.Load());
        Saved?.Invoke();
    }

    partial void OnSendUsageStatsChanged(bool value)
    {
        if (_loading) return;
        _store.Save(UsageStats.With(_store.Load(), value));
        UsageStats.Apply(_store.Load());
        Saved?.Invoke();
    }
    partial void OnDictationModelChanged(string value) => Save();
    partial void OnDictationLanguageChanged(string value) => Save();

    private void Save()
    {
        if (_loading) return;
        // Read first: the main window saves panels and the theme into the same file
        _store.Save(_store.Load() with
        {
            Theme = _applyTheme == null ? Theme : _store.Load().Theme,
            TitlesFromClaude = TitlesFromClaude,
            NotifyWhenWaiting = NotifyWhenWaiting,
            NotifyWhenDone = NotifyWhenDone,
            KeepAwakeWhileWorking = KeepAwakeWhileWorking,
            StatusLight = StatusLight,
            OpenWebLinksInPreview = OpenWebLinksInPreview,
            StartAgentsOnOpen = StartAgentsOnOpen,
            CheckForUpdates = CheckForUpdates,
            DictationModel = DictationModel,
            DictationLanguage = DictationLanguage,
        });
        Saved?.Invoke();
    }

    /// <summary>A setting was written (the main window re-reads what it keeps in memory).</summary>
    public event Action? Saved;
}
