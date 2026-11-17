namespace VeganAdda.Contracts;

/// <summary>A value for one UTC day.</summary>
public record DayCount(DateOnly Day, int Count);
/// <summary>A zero-filled daily series. Key is stable; Label is display text.</summary>
public record StatsSeries(string Key, string Label, List<DayCount> Days)
{
    public int Total => Days.Sum(d => d.Count);
}
/// <summary>Active members in the 1, 7 and 30 days ending on the report's last day.</summary>
public record EngagementSummary(int Dau, int Wau, int Mau, double? Stickiness, int Members);
public record FunnelStep(string Label, int Count);
/// <summary>Signup-week cohort. Retention fractions are null until measurable.</summary>
public record RetentionCohort(DateOnly WeekStart, int Size, double? Day1, double? Day7, double? Day30);
public record StatsReport(DateOnly From, DateOnly To, EngagementSummary Engagement, List<DayCount> DailyActive,
    List<StatsSeries> Series, List<FunnelStep> Funnel, List<RetentionCohort> Retention,
    DateOnly? ActivitySince, DateOnly? CountersSince);
public interface IStatsApi
{
    Task<StatsReport> GetStats(int days);
}
