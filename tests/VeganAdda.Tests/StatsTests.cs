using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Stats;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

// Aggregates are global, so each test uses its own database.
public class StatsTests
{
    private static async Task<DatabaseFixture> Isolated() { var f = new DatabaseFixture(); await f.InitializeAsync(); return f; }
    private static DateOnly Today => ActivityRecorder.Today;

    private static async Task Active(DatabaseFixture f, Guid id, params int[] daysAgo)
    {
        await using var db = f.Db();
        foreach (var d in daysAgo) db.AccountActivity.Add(new() { AccountId = id, Day = Today.AddDays(-d) });
        await db.SaveChangesAsync();
    }
    private static async Task Mark(DatabaseFixture f, Guid id, Action<Account, Profile> change)
    {
        await using var db = f.Db();
        change(await db.Accounts.SingleAsync(a => a.Id == id), await db.Profiles.SingleAsync(p => p.Id == id));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Dau_wau_mau_count_distinct_real_members_only()
    {
        var f = await Isolated();
        try
        {
            var admin = await f.Member(admin: true);
            var a = await f.Member(); var b = await f.Member(); var demo = await f.Member(); var test = await f.Member(); var suspended = await f.Member();
            await Mark(f, demo, (x, _) => x.Demo = true);
            await Mark(f, test, (_, p) => p.TestProfile = true);
            await Mark(f, suspended, (x, _) => x.Suspended = true);
            await Active(f, a, 0, 3, 20); await Active(f, b, 3, 40); await Active(f, demo, 0); await Active(f, test, 0, 1); await Active(f, suspended, 0);
            await Active(f, admin, 0);
            var s = await f.Feature<StatsService, StatsReport>(admin, x => x.GetStats(30));
            Assert.Equal(2, s.Engagement.Dau);            // a and admin
            Assert.Equal(3, s.Engagement.Wau);            // a, b, admin
            Assert.Equal(3, s.Engagement.Mau);            // a, b, admin (b's day -40 is outside)
            Assert.Equal(Math.Round(2 / 3.0, 3), s.Engagement.Stickiness);
            Assert.Equal(30, s.DailyActive.Count);
            Assert.Equal(2, s.DailyActive[^1].Count);
            Assert.Equal(Today, s.To);
            Assert.Equal(Today.AddDays(-29), s.From);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Stats_are_admin_only()
    {
        var f = await Isolated();
        try
        {
            var member = await f.Member();
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => f.Feature<StatsService, StatsReport>(member, x => x.GetStats(30)))).Status);
            Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => f.Feature<StatsService, StatsReport>(null, x => x.GetStats(30)))).Status);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Touch_records_once_per_day_and_account_deletion_removes_activity()
    {
        var f = await Isolated();
        try
        {
            var id = await f.Member();
            for (var i = 0; i < 3; i++) await f.Feature<ActivityRecorder, int>(null, async r => { await r.Touch(id); return 0; });
            await using (var db = f.Db()) Assert.Equal(1, await db.AccountActivity.CountAsync(x => x.AccountId == id && x.Day == Today));
            await using (var db = f.Db()) { await db.Accounts.Where(a => a.Id == id).ExecuteDeleteAsync(); Assert.Equal(0, await db.AccountActivity.CountAsync()); }
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Counters_skip_test_demo_and_suspended_actors_and_hold_no_account_ids()
    {
        var f = await Isolated();
        try
        {
            var admin = await f.Member(admin: true);
            var real = await f.Member(); var test = await f.Member(); var demo = await f.Member(); var suspended = await f.Member();
            await Mark(f, test, (_, p) => p.TestProfile = true);
            await Mark(f, demo, (x, _) => x.Demo = true);
            await Mark(f, suspended, (x, _) => x.Suspended = true);
            foreach (var id in new[] { real, real, test, demo, suspended })
                await f.Feature<ActivityRecorder, int>(null, async r => { await r.Count(ActivityKind.EventJoined, id); return 0; });
            await using (var db = f.Db())
            {
                var row = await db.ActivityCounters.SingleAsync();
                Assert.Equal((Today, ActivityKind.EventJoined, 2L), (row.Day, row.Kind, row.Count));
            }
            var s = await f.Feature<StatsService, StatsReport>(admin, x => x.GetStats(7));
            Assert.Equal(2, s.Series.Single(x => x.Key == "event_joins").Total);
            Assert.Equal(Today, s.CountersSince);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Connection_counters_skip_when_either_participant_is_excluded()
    {
        var f = await Isolated();
        try
        {
            var real = await f.Member(); var other = await f.Member(); var test = await f.Member();
            await Mark(f, test, (_, p) => p.TestProfile = true);
            await f.Feature<ActivityRecorder, int>(null, async r => { await r.Count(ActivityKind.ConnectionRequested, real, test); await r.Count(ActivityKind.ConnectionRequested, test, real); await r.Count(ActivityKind.ConnectionRequested, real, other); return 0; });
            await using var db = f.Db();
            Assert.Equal(1L, (await db.ActivityCounters.SingleAsync()).Count);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Reports_exclude_rows_involving_excluded_accounts()
    {
        var f = await Isolated();
        try
        {
            var admin = await f.Member(admin: true); var a = await f.Member(); var test = await f.Member(); var demo = await f.Member();
            await Mark(f, test, (_, p) => p.TestProfile = true);
            await Mark(f, demo, (x, _) => x.Demo = true);
            await using (var db = f.Db())
            {
                db.Reports.AddRange(new Report { ReporterId = a, SubjectId = admin, Reason = "x" }, new Report { ReporterId = test, SubjectId = admin, Reason = "x" },
                    new Report { ReporterId = admin, SubjectId = test, Reason = "x" }, new Report { ReporterId = a, SubjectId = demo, Reason = "x" });
                await db.SaveChangesAsync();
            }
            var s = await f.Feature<StatsService, StatsReport>(admin, x => x.GetStats(7));
            Assert.Equal(1, s.Series.Single(x => x.Key == "reports").Total);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Signups_and_messages_exclude_rows_involving_test_members()
    {
        var f = await Isolated();
        try
        {
            var admin = await f.Member(admin: true);
            var a = await f.Member(); var b = await f.Member(); var test = await f.Member();
            await Mark(f, test, (_, p) => p.TestProfile = true);
            await using (var db = f.Db())
            {
                var content = new ChatContent(f.ChatEncryption, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatContent>.Instance);
                foreach (var (x, y) in new[] { (a, b), (a, test) })
                {
                    var key = VeganAdda.Web.Features.Shared.FeatureService.Pair(x, y);
                    var (lo, hi) = string.CompareOrdinal(x.ToString(), y.ToString()) < 0 ? (x, y) : (y, x);
                    db.Relationships.Add(new() { Key = key, A = lo, B = hi, State = "accepted", RequestedBy = x });
                    db.Messages.Add(content.NewMessage(key, x, Guid.NewGuid(), "hi"));
                }
                await db.SaveChangesAsync();
            }
            var s = await f.Feature<StatsService, StatsReport>(admin, x => x.GetStats(7));
            Assert.Equal(1, s.Series.Single(x => x.Key == "messages").Total);
            Assert.Equal(3, s.Series.Single(x => x.Key == "signups").Total);   // admin, a, b; test excluded
            Assert.Equal(3, s.Funnel[0].Count);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Retention_reports_a_rate_only_when_the_whole_cohort_is_old_enough()
    {
        var f = await Isolated();
        try
        {
            var admin = await f.Member(admin: true);
            var kept = await f.Member(); var lost = await f.Member(); var fresh = await f.Member();
            var signup = DateTimeOffset.UtcNow.AddDays(-10);
            await using (var db = f.Db())
            {
                await db.Accounts.Where(x => x.Id == kept || x.Id == lost).ExecuteUpdateAsync(s => s.SetProperty(x => x.Created, signup));
                await db.Accounts.Where(x => x.Id == fresh).ExecuteUpdateAsync(s => s.SetProperty(x => x.Created, DateTimeOffset.UtcNow));
                await db.Accounts.Where(x => x.Id == admin).ExecuteUpdateAsync(s => s.SetProperty(x => x.Created, DateTimeOffset.UtcNow.AddYears(-1)));
            }
            var signupDay = DateOnly.FromDateTime(signup.UtcDateTime);
            await using (var db = f.Db())
            {
                db.AccountActivity.AddRange(new AccountActivity { AccountId = kept, Day = signupDay }, new AccountActivity { AccountId = kept, Day = signupDay.AddDays(1) },
                    new AccountActivity { AccountId = lost, Day = signupDay });
                await db.SaveChangesAsync();
            }
            var s = await f.Feature<StatsService, StatsReport>(admin, x => x.GetStats(30));
            var cohort = s.Retention.Single(c => c.Size == 2);
            Assert.Equal(0.5, cohort.Day1);
            Assert.Equal(0.0, cohort.Day7);
            Assert.Null(cohort.Day30);
            Assert.Null(s.Retention.Single(c => c.Size == 1).Day1);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Retention_waits_for_the_observation_day_to_finish()
    {
        var f = await Isolated();
        try
        {
            var admin = await f.Member(admin: true); var yesterday = await f.Member();
            await using (var db = f.Db())
            {
                await db.Accounts.Where(x => x.Id == admin).ExecuteUpdateAsync(u => u.SetProperty(x => x.Created, DateTimeOffset.UtcNow.AddYears(-1)));
                await db.Accounts.Where(x => x.Id == yesterday).ExecuteUpdateAsync(u => u.SetProperty(x => x.Created, DateTimeOffset.UtcNow.AddDays(-1)));
                db.AccountActivity.AddRange(new AccountActivity { AccountId = admin, Day = Today.AddDays(-3) }, new AccountActivity { AccountId = yesterday, Day = Today });
                await db.SaveChangesAsync();
            }
            // Day 1 for a member created yesterday is today, which has not finished: no rate, even though they are active today.
            var cohort = Assert.Single((await f.Feature<StatsService, StatsReport>(admin, x => x.GetStats(30))).Retention);
            Assert.Equal(1, cohort.Size);
            Assert.Null(cohort.Day1);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Retention_job_removes_activity_older_than_400_days_but_keeps_counters()
    {
        var f = await Isolated();
        try
        {
            var id = await f.Member();
            await Active(f, id, 0, DataRetentionWorker.ActivityRetentionDays + 1);
            await using var db = f.Db();
            db.ActivityCounters.Add(new() { Day = Today.AddDays(-500), Kind = ActivityKind.SignIn, Count = 5 });
            await db.SaveChangesAsync();
            await DataRetentionWorker.DeleteExpiredActivity(db, CancellationToken.None);
            Assert.Equal(1, await db.AccountActivity.CountAsync());
            Assert.Equal(1, await db.ActivityCounters.CountAsync());
        }
        finally { await f.DisposeAsync(); }
    }
}
