using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KovanTerm.Services;
using KovanTerm.Services.Stats;

namespace KovanTerm.ViewModels;

public enum StatsPeriod { Day, Week, Month, Year }

public enum StatsMetric { Time, Tokens }

/// <summary>A day on the year calendar.</summary>
/// <param name="Level">0 (nothing) to 4 (one of the busiest days), for its shade.</param>
public sealed record CalendarDay(DateOnly Date, int Level, string Tooltip, bool IsFuture, bool IsSelected);

/// <summary>A week's column on the calendar, Monday first; <see cref="Month"/> labels the first week of a month.</summary>
public sealed record CalendarWeek(IReadOnlyList<CalendarDay> Days, string? Month);

/// <param name="Slot">Its color: 1–7 for the most active projects, 0 for the rest.</param>
public sealed record ProjectRow(string Name, int Slot, TimeSpan Time, long Tokens, int Sessions, double TimeFraction, double TokensFraction,
    int Commits = 0)
{
    public string TimeText => StatisticsViewModel.FormatTime(Time);
    public string TokensText => StatisticsViewModel.FormatTokens(Tokens);
}

public sealed record DayBar(DateOnly Date, string Label, double Hours, double Fraction, string Tooltip, bool IsSelected);

public sealed record ModelRow(string Model, long Tokens, double Fraction)
{
    public string TokensText => StatisticsViewModel.FormatTokens(Tokens);
}

public sealed record CommitRow(string When, string Project, int Slot, string Subject);

public sealed record SessionRow(WorkSession Session, string When, string Project, int Slot, string Agent, string Title, string Duration,
    string Tokens, IReadOnlyList<string> Prompts);

/// <summary>
/// Statistics (#88): where the time and tokens went, from Claude Code's and Codex's own session files. A period (a day
/// picked on the calendar, the last 7 or 30 days, the last year) filters everything but the calendar and the daily bars.
/// </summary>
/// <param name="loadCommits">The commits in the sessions' repositories, read after the sessions (git is slower); none if <c>null</c>.</param>
public sealed partial class StatisticsViewModel(Func<IProgress<(int Done, int Total)>, List<WorkSession>> load, Func<DateTime>? now = null,
    Func<IReadOnlyList<WorkSession>, Task<List<WorkCommit>>>? loadCommits = null)
    : ObservableObject
{
    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now);
    private List<WorkSession> _sessions = [];
    private List<WorkCommit> _commits = [];
    /// <summary>Until git has answered, the commit count is "…", not 0.</summary>
    private bool _commitsLoaded;
    /// <summary>A repository's project, as the sessions name it.</summary>
    private Dictionary<string, string> _projectOfRepo = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _slots = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string LoadingText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodText))]
    public partial StatsPeriod Period { get; set; } = StatsPeriod.Week;

    /// <summary>The day picked on the calendar or the bars (<see cref="StatsPeriod.Day"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodText))]
    public partial DateOnly SelectedDay { get; set; }

    [ObservableProperty]
    public partial StatsMetric Metric { get; set; } = StatsMetric.Time;

    public string PeriodText => Period switch
    {
        StatsPeriod.Day => SelectedDay.ToString("dddd, d MMMM yyyy", Ui),
        StatsPeriod.Week => "Last 7 days",
        StatsPeriod.Month => "Last 30 days",
        _ => "Last 12 months",
    };

    // ---- What's shown ----

    [ObservableProperty] public partial string ActiveTime { get; set; } = "";
    [ObservableProperty] public partial string ActiveDays { get; set; } = "";
    [ObservableProperty] public partial string ProjectCount { get; set; } = "";
    [ObservableProperty] public partial string SessionCount { get; set; } = "";
    [ObservableProperty] public partial string TokenCount { get; set; } = "";
    [ObservableProperty] public partial string CommitCount { get; set; } = "…";
    public ObservableCollection<CommitRow> Commits { get; } = [];
    [ObservableProperty] public partial string TokenDetail { get; set; } = "";
    [ObservableProperty] public partial string CalendarSummary { get; set; } = "";

    public ObservableCollection<CalendarWeek> Weeks { get; } = [];
    public ObservableCollection<ProjectRow> Projects { get; } = [];
    public ObservableCollection<DayBar> Days { get; } = [];
    public ObservableCollection<ModelRow> Models { get; } = [];
    public ObservableCollection<SessionRow> Log { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadingText = "Reading conversations…";
        var progress = new Progress<(int Done, int Total)>(p => LoadingText = $"Reading conversations… {p.Done} of {p.Total}");
        _sessions = await Task.Run(() => load(progress));
        // Colors follow the project, not its rank in the period: the 7 with the most work overall
        _slots = _sessions.GroupBy(s => s.Project).OrderByDescending(g => g.Sum(s => s.ActiveTime.Ticks)).Take(7)
            .Select((g, i) => (g.Key, i + 1)).ToDictionary(x => x.Key, x => x.Item2);
        SelectedDay = DateOnly.FromDateTime(_now());
        IsLoading = false;
        Update();
        if (loadCommits == null)
        {
            CommitCount = "";
            return;
        }
        _commits = await loadCommits(_sessions);
        _commitsLoaded = true;
        _projectOfRepo = new(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _sessions)
            if (Services.GitWorktrees.FindRepoRoot(s.Cwd) is { } repo) _projectOfRepo.TryAdd(repo, s.Project);
        Update();
    }

    private string ProjectOf(WorkCommit c) => _projectOfRepo.GetValueOrDefault(c.Repository) ?? System.IO.Path.GetFileName(c.Repository);

    [RelayCommand]
    private void ShowPeriod(StatsPeriod period)
    {
        Period = period;
        Update();
    }

    [RelayCommand]
    private void ShowDay(DateOnly day)
    {
        if (day > DateOnly.FromDateTime(_now())) return;
        SelectedDay = day;
        Period = StatsPeriod.Day;
        Update();
    }

    [RelayCommand]
    private void ShowMetric(StatsMetric metric)
    {
        Metric = metric;
        UpdateCalendar();
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    /// <summary>[from, to) of the period, as days.</summary>
    public (DateOnly From, DateOnly To) Range()
    {
        var today = DateOnly.FromDateTime(_now());
        return Period switch
        {
            StatsPeriod.Day => (SelectedDay, SelectedDay.AddDays(1)),
            StatsPeriod.Week => (today.AddDays(-6), today.AddDays(1)),
            StatsPeriod.Month => (today.AddDays(-29), today.AddDays(1)),
            _ => (today.AddDays(-364), today.AddDays(1)),
        };
    }

    private void Update()
    {
        var (from, to) = Range();
        var fromTime = from.ToDateTime(TimeOnly.MinValue);
        var toTime = to.ToDateTime(TimeOnly.MinValue);
        var inPeriod = _sessions.Where(s => s.Spans.Any(sp => sp.End > fromTime && sp.Start < toTime)
                                            || s.TokenDays.Any(d => d.Day >= from && d.Day < to)).ToList();
        var spans = WorkStats.Union(inPeriod.SelectMany(s => WorkStats.Clip(s.Spans, fromTime, toTime)));
        var tokens = inPeriod.Aggregate(WorkTokens.Zero, (a, s) => a + s.TokensIn(from, to));

        ActiveTime = FormatTime(TimeSpan.FromTicks(spans.Sum(s => s.Duration.Ticks)));
        ActiveDays = WorkStats.PerDay(spans).Count.ToString(Ui);
        ProjectCount = inPeriod.Select(s => s.Project).Distinct().Count().ToString(Ui);
        SessionCount = inPeriod.Count.ToString(Ui);
        TokenCount = StatisticsViewModel.FormatTokens(tokens.Total);
        TokenDetail = $"{StatisticsViewModel.FormatTokens(tokens.CacheRead)} read from cache · {StatisticsViewModel.FormatTokens(tokens.CacheWrite)} written to it · "
                      + $"{StatisticsViewModel.FormatTokens(tokens.Input)} in · {StatisticsViewModel.FormatTokens(tokens.Output)} out";
        IsEmpty = _sessions.Count == 0;
        var commits = _commits.Where(c => c.Date >= fromTime && c.Date < toTime).OrderByDescending(c => c.Date).ToList();
        if (_commitsLoaded) CommitCount = commits.Count.ToString(Ui);
        Replace(Commits, commits.Take(100).Select(c => new CommitRow(
            Period == StatsPeriod.Day ? c.Date.ToString("HH:mm", Ui) : c.Date.ToString("d MMM HH:mm", Ui),
            ProjectOf(c), _slots.GetValueOrDefault(ProjectOf(c)), c.Subject)));
        var commitsByProject = commits.GroupBy(ProjectOf).ToDictionary(g => g.Key, g => g.Count());

        // Projects: time and tokens in the period
        var projects = inPeriod.GroupBy(s => s.Project).Select(g => (
                Name: g.Key,
                Time: TimeSpan.FromTicks(WorkStats.Union(g.SelectMany(s => WorkStats.Clip(s.Spans, fromTime, toTime))).Sum(s => s.Duration.Ticks)),
                Tokens: g.Sum(s => s.TokensIn(from, to).Total),
                Sessions: g.Count()))
            .OrderByDescending(p => p.Time).ThenByDescending(p => p.Tokens).ToList();
        var maxTime = Math.Max(1, projects.Select(p => p.Time.Ticks).DefaultIfEmpty(0).Max());
        var maxTokens = Math.Max(1, projects.Select(p => p.Tokens).DefaultIfEmpty(0).Max());
        Replace(Projects, projects.Select(p => new ProjectRow(p.Name, _slots.GetValueOrDefault(p.Name), p.Time, p.Tokens, p.Sessions,
            (double)p.Time.Ticks / maxTime, (double)p.Tokens / maxTokens, commitsByProject.GetValueOrDefault(p.Name))));

        // Models: tokens in the period
        var models = inPeriod.SelectMany(s => s.TokenDays).Where(d => d.Day >= from && d.Day < to)
            .GroupBy(d => d.Model).Select(g => (Model: g.Key, Tokens: g.Sum(d => d.Tokens.Total)))
            .Where(m => m.Tokens > 0).OrderByDescending(m => m.Tokens).ToList();
        var maxModel = Math.Max(1, models.Select(m => m.Tokens).DefaultIfEmpty(0).Max());
        Replace(Models, models.Select(m => new ModelRow(m.Model, m.Tokens, (double)m.Tokens / maxModel)));

        // The log: each session in the period, latest first
        Replace(Log, inPeriod.OrderByDescending(s => s.End).Take(200).Select(s =>
        {
            var clipped = WorkStats.Clip(s.Spans, fromTime, toTime).ToList();
            var start = clipped.Count > 0 ? clipped[0].Start : s.Start;
            var end = clipped.Count > 0 ? clipped[^1].End : s.End;
            var when = Period == StatsPeriod.Day ? $"{start:HH:mm}–{end:HH:mm}" : start.ToString("d MMM HH:mm", Ui);
            return new SessionRow(s, when, s.Project, _slots.GetValueOrDefault(s.Project), s.Agent == "codex" ? "Codex" : "Claude",
                s.Title, FormatTime(TimeSpan.FromTicks(clipped.Sum(c => c.Duration.Ticks))), StatisticsViewModel.FormatTokens(s.TokensIn(from, to).Total),
                s.Prompts.Where(p => p.Date >= fromTime && p.Date < toTime).Select(p => $"{p.Date:HH:mm}  {p.Text}").ToList());
        }));

        UpdateDays();
        UpdateCalendar();
    }

    /// <summary>Hours worked on each of the last 14 days (ending on the selected day when one is picked).</summary>
    private void UpdateDays()
    {
        var today = DateOnly.FromDateTime(_now());
        var last = Period == StatsPeriod.Day ? SelectedDay : today;
        var perDay = WorkStats.PerDay(WorkStats.Union(_sessions.SelectMany(s => s.Spans)));
        var days = Enumerable.Range(0, 14).Select(i => last.AddDays(i - 13)).ToList();
        var max = Math.Max(1, days.Max(d => perDay.GetValueOrDefault(d).TotalHours));
        Replace(Days, days.Select(d =>
        {
            var hours = perDay.GetValueOrDefault(d).TotalHours;
            return new DayBar(d, d.ToString("ddd", Ui)[..2] + "\n" + d.Day, hours, hours / max,
                $"{d.ToString("dddd d MMMM", Ui)}: {FormatTime(perDay.GetValueOrDefault(d))}", Period == StatsPeriod.Day && d == SelectedDay);
        }));
    }

    /// <summary>53 weeks up to this one, shaded by time worked or tokens.</summary>
    private void UpdateCalendar()
    {
        var today = DateOnly.FromDateTime(_now());
        var perDayTime = WorkStats.PerDay(WorkStats.Union(_sessions.SelectMany(s => s.Spans)));
        var perDayTokens = _sessions.SelectMany(s => s.TokenDays).GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.Sum(d => d.Tokens.Total));
        double Value(DateOnly d) => Metric == StatsMetric.Time ? perDayTime.GetValueOrDefault(d).TotalHours : perDayTokens.GetValueOrDefault(d);

        // Monday of the week 52 weeks before this one
        var start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 52 * 7);
        var values = Enumerable.Range(0, 53 * 7).Select(i => start.AddDays(i)).Where(d => d <= today).Select(Value).Where(v => v > 0).Order().ToList();
        double Quantile(double q) => values.Count == 0 ? 0 : values[(int)Math.Min(values.Count - 1, Math.Floor(q * values.Count))];
        var (q1, q2, q3) = (Quantile(0.25), Quantile(0.5), Quantile(0.75));
        int Level(double v) => v <= 0 ? 0 : v <= q1 ? 1 : v <= q2 ? 2 : v <= q3 ? 3 : 4;

        var weeks = new List<CalendarWeek>();
        for (var w = 0; w < 53; w++)
        {
            var monday = start.AddDays(w * 7);
            var days = Enumerable.Range(0, 7).Select(i =>
            {
                var d = monday.AddDays(i);
                var v = Value(d);
                var text = Metric == StatsMetric.Time ? FormatTime(perDayTime.GetValueOrDefault(d)) : $"{StatisticsViewModel.FormatTokens((long)v)} tokens";
                return new CalendarDay(d, Level(v), $"{d.ToString("dddd d MMMM yyyy", Ui)}: {text}", d > today, Period == StatsPeriod.Day && d == SelectedDay);
            }).ToList();
            // A month's name over the week its first day falls in
            var month = days.FirstOrDefault(d => d.Date.Day == 1) is { } first ? first.Date.ToString("MMM", Ui) : null;
            weeks.Add(new CalendarWeek(days, month));
        }
        Replace(Weeks, weeks);
        var activeDays = perDayTime.Keys.Count(d => d >= start && d <= today);
        CalendarSummary = Metric == StatsMetric.Time
            ? $"{FormatTime(TimeSpan.FromHours(perDayTime.Where(p => p.Key >= start).Sum(p => p.Value.TotalHours)))} on {activeDays} days in the last year"
            : $"{StatisticsViewModel.FormatTokens(perDayTokens.Where(p => p.Key >= start).Sum(p => p.Value))} tokens in the last year";
    }

    /// <summary>Dates in the app's language (English), like the rest of its text.</summary>
    private static readonly CultureInfo Ui = CultureInfo.InvariantCulture;

    /// <summary>"812k", "81.2M", "3.0B".</summary>
    public static string FormatTokens(long tokens) =>
        tokens >= 1_000_000_000 ? string.Create(CultureInfo.InvariantCulture, $"{tokens / 1e9:0.0}B") : TranscriptUsage.Format(tokens);

    public static string FormatTime(TimeSpan t) => t.TotalMinutes < 1 ? "0 min"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : $"{(int)t.TotalHours} h {t.Minutes:00} min";

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
