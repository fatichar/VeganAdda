using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Features.Stats;

/// <summary>
/// Admin-only aggregate metrics. Every query filters and groups in the database and returns counts;
/// no member identity, message content or location leaves this service. Demo, suspended and
/// test-profile accounts are excluded, and a relationship or message is excluded when either participant is.
/// </summary>
public sealed class StatsService(FeatureContext context) : FeatureService(context)
{
    public const int MaxDays = 365;
    private const int RetentionWeeks = 12;

    private sealed record DayRow(DateOnly Day, int Count);
    private sealed record RetentionRow(DateOnly Week, int Size, int Eligible1, int Retained1, int Eligible7, int Retained7, int Eligible30, int Retained30);

    public async Task<StatsReport> GetStats(int days = 30)
    {
        await User(admin: true, allowIncomplete: true);
        days = Math.Clamp(days, 7, MaxDays);
        var to = ActivityRecorder.Today;
        var from = to.AddDays(1 - days);
        var fromTime = Start(from);
        var toTime = Start(to.AddDays(1));

        var excluded = db.Accounts.Where(a => a.Demo || a.Suspended || db.Profiles.Any(p => p.Id == a.Id && p.TestProfile)).Select(a => a.Id);
        var real = db.Accounts.Where(a => !a.Demo && !a.Suspended && !db.Profiles.Any(p => p.Id == a.Id && p.TestProfile));
        var activity = db.AccountActivity.Where(x => !excluded.Contains(x.AccountId));

        var daily = await activity.Where(x => x.Day >= from && x.Day <= to)
            .GroupBy(x => x.Day).Select(g => new DayCount(g.Key, g.Count())).ToListAsync();
        var dau = await activity.Where(x => x.Day == to).CountAsync();
        var wau = await activity.Where(x => x.Day >= to.AddDays(-6) && x.Day <= to).Select(x => x.AccountId).Distinct().CountAsync();
        var mau = await activity.Where(x => x.Day >= to.AddDays(-29) && x.Day <= to).Select(x => x.AccountId).Distinct().CountAsync();
        var members = await real.CountAsync();

        // Per-day counts use SQL with an explicit UTC day so they match the UTC activity days.
        var signups = await db.Database.SqlQuery<DayRow>($"""
            SELECT (a."Created" AT TIME ZONE 'UTC')::date AS "Day", count(*)::int AS "Count"
            FROM "Accounts" a
            WHERE a."Created" >= {fromTime} AND a."Created" < {toTime} AND NOT a."Demo" AND NOT a."Suspended"
              AND NOT EXISTS (SELECT 1 FROM "Profiles" p WHERE p."Id" = a."Id" AND p."TestProfile")
            GROUP BY 1
            """).ToListAsync();
        var messages = await db.Database.SqlQuery<DayRow>($"""
            SELECT (m."SentAt" AT TIME ZONE 'UTC')::date AS "Day", count(*)::int AS "Count"
            FROM "Messages" m JOIN "Relationships" r ON r."Key" = m."Conversation"
            WHERE m."SentAt" >= {fromTime} AND m."SentAt" < {toTime}
              AND NOT EXISTS (SELECT 1 FROM "Accounts" a LEFT JOIN "Profiles" p ON p."Id" = a."Id"
                              WHERE a."Id" IN (r."A", r."B") AND (a."Demo" OR a."Suspended" OR COALESCE(p."TestProfile", FALSE)))
            GROUP BY 1
            """).ToListAsync();
        var reports = await db.Database.SqlQuery<DayRow>($"""
            SELECT (r."Created" AT TIME ZONE 'UTC')::date AS "Day", count(*)::int AS "Count"
            FROM "Reports" r
            WHERE r."Created" >= {fromTime} AND r."Created" < {toTime}
              AND NOT EXISTS (SELECT 1 FROM "Accounts" a LEFT JOIN "Profiles" p ON p."Id" = a."Id"
                              WHERE a."Id" IN (r."ReporterId", r."SubjectId") AND (a."Demo" OR a."Suspended" OR COALESCE(p."TestProfile", FALSE)))
            GROUP BY 1
            """).ToListAsync();
        var counters = await db.ActivityCounters.Where(c => c.Day >= from && c.Day <= to)
            .Select(c => new { c.Day, c.Kind, c.Count }).ToListAsync();

        List<DayCount> Fill(IEnumerable<(DateOnly Day, int N)> rows)
        {
            var map = rows.GroupBy(r => r.Day).ToDictionary(g => g.Key, g => g.Sum(r => r.N));
            return Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(d => new DayCount(d, map.GetValueOrDefault(d))).ToList();
        }
        List<DayCount> FromCounter(string kind) =>
            Fill(counters.Where(c => c.Kind == kind).Select(c => (c.Day, (int)Math.Min(c.Count, int.MaxValue))));
        List<DayCount> FromRows(List<DayRow> rows) => Fill(rows.Select(r => (r.Day, r.Count)));

        var series = new List<StatsSeries>
        {
            new("signups", "New accounts", FromRows(signups)),
            new("sign_ins", "Sign-ins", FromCounter(ActivityKind.SignIn)),
            new("profiles", "Profiles created", FromCounter(ActivityKind.ProfileCreated)),
            new("verification_requests", "Verification requests", FromCounter(ActivityKind.VerificationRequested)),
            new("verified", "Vegan statuses verified", FromCounter(ActivityKind.VeganVerified)),
            new("connection_requests", "Connection requests", FromCounter(ActivityKind.ConnectionRequested)),
            new("connections", "Connections accepted", FromCounter(ActivityKind.ConnectionAccepted)),
            new("messages", "Messages sent", FromRows(messages)),
            new("event_joins", "Event attendance", FromCounter(ActivityKind.EventJoined)),
            new("reports", "Reports opened", FromRows(reports)),
        };

        var profiles = db.Profiles.Where(p => !excluded.Contains(p.Id));
        var funnel = new List<FunnelStep>
        {
            new("Accounts", members),
            new("Complete basic profile", await profiles.Where(BasicProfile.Complete).CountAsync()),
            new("Vegan", await profiles.Where(BasicProfile.Complete).CountAsync(p => p.IsVegan == true)),
            new("Verification requested", await db.VerificationRequests.Where(r => !excluded.Contains(r.ApplicantId)).Select(r => r.ApplicantId).Distinct().CountAsync()),
            new("Verified vegan", await profiles.CountAsync(p => p.VeganVerified)),
        };

        var activitySince = await db.AccountActivity.MinAsync(x => (DateOnly?)x.Day);
        var countersSince = await db.ActivityCounters.MinAsync(x => (DateOnly?)x.Day);
        return new(from, to, new(dau, wau, mau, mau == 0 ? null : Math.Round((double)dau / mau, 3), members),
            Fill(daily.Select(d => (d.Day, d.Count))), series, funnel, await Retention(to, activitySince), activitySince, countersSince);
    }

    // Signup-week cohorts retained on exactly day 1, 7 and 30 after signup. Only cohorts created after
    // activity tracking began are measured; a day-n rate appears once the observation day has fully ended for every member.
    private async Task<List<RetentionCohort>> Retention(DateOnly today, DateOnly? trackingStart)
    {
        if (trackingStart is not { } begin) return [];
        var since = Start(begin);
        var oldest = Start(today.AddDays(-7 * RetentionWeeks));
        if (oldest > since) since = oldest;
        var rows = await db.Database.SqlQuery<RetentionRow>($"""
            SELECT date_trunc('week', a."Created" AT TIME ZONE 'UTC')::date AS "Week",
                   count(*)::int AS "Size",
                   (count(*) FILTER (WHERE (a."Created" AT TIME ZONE 'UTC')::date + 1 < {today}))::int AS "Eligible1",
                   (count(*) FILTER (WHERE EXISTS (SELECT 1 FROM "AccountActivity" x WHERE x."AccountId" = a."Id" AND x."Day" = (a."Created" AT TIME ZONE 'UTC')::date + 1)))::int AS "Retained1",
                   (count(*) FILTER (WHERE (a."Created" AT TIME ZONE 'UTC')::date + 7 < {today}))::int AS "Eligible7",
                   (count(*) FILTER (WHERE EXISTS (SELECT 1 FROM "AccountActivity" x WHERE x."AccountId" = a."Id" AND x."Day" = (a."Created" AT TIME ZONE 'UTC')::date + 7)))::int AS "Retained7",
                   (count(*) FILTER (WHERE (a."Created" AT TIME ZONE 'UTC')::date + 30 < {today}))::int AS "Eligible30",
                   (count(*) FILTER (WHERE EXISTS (SELECT 1 FROM "AccountActivity" x WHERE x."AccountId" = a."Id" AND x."Day" = (a."Created" AT TIME ZONE 'UTC')::date + 30)))::int AS "Retained30"
            FROM "Accounts" a
            WHERE a."Created" >= {since} AND NOT a."Demo" AND NOT a."Suspended"
              AND NOT EXISTS (SELECT 1 FROM "Profiles" p WHERE p."Id" = a."Id" AND p."TestProfile")
            GROUP BY 1 ORDER BY 1 DESC
            """).ToListAsync();
        static double? Rate(int retained, int eligible, int size) => eligible == size && size > 0 ? Math.Round((double)retained / size, 3) : null;
        return rows.Select(r => new RetentionCohort(r.Week, r.Size, Rate(r.Retained1, r.Eligible1, r.Size),
            Rate(r.Retained7, r.Eligible7, r.Size), Rate(r.Retained30, r.Eligible30, r.Size))).ToList();
    }

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
