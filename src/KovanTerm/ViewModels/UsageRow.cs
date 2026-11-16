using KovanTerm.Services;

namespace KovanTerm.ViewModels;

/// <summary>A usage limit as the Agents panel shows it: "5h", a bar, "26 % · resets in 2 h 10 m".</summary>
public sealed class UsageRow(UsageLimit limit, DateTimeOffset now)
{
    public string Label => limit.Label;
    public double Percent => limit.Percent;
    public bool IsHigh => limit.IsHigh;
    public string Text => limit.ResetsIn(now) is { } r ? $"{limit.Percent:0} % · resets {r}" : $"{limit.Percent:0} %";
    public string? ResetsAtText => limit.ResetsAt?.ToLocalTime().ToString("ddd HH:mm");
}
