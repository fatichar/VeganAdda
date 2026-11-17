using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

/// <summary>Counter kinds. Stable strings: they are stored.</summary>
public static class ActivityKind
{
    public const string SignIn = "sign_in";
    public const string ProfileCreated = "profile_created";
    public const string ConnectionRequested = "connection_requested";
    public const string ConnectionAccepted = "connection_accepted";
    public const string EventJoined = "event_joined";
    public const string VerificationRequested = "verification_requested";
    public const string VeganVerified = "vegan_verified";
}

/// <summary>Remembers which accounts were already recorded today so a request costs no write.</summary>
public sealed class ActivityTracker
{
    private const int MaxEntries = 100_000;
    private readonly ConcurrentDictionary<Guid, DateOnly> seen = new();
    public bool Seen(Guid id, DateOnly day) => seen.TryGetValue(id, out var d) && d == day;
    public void Mark(Guid id, DateOnly day)
    {
        if (seen.Count >= MaxEntries) seen.Clear();
        seen[id] = day;
    }
}

/// <summary>
/// Best-effort product analytics stored in our database. Records account id plus UTC day only, and
/// anonymous per-day transition counts. Never records content, locations or free text, and never throws.
/// </summary>
public sealed class ActivityRecorder(CommunityDb db, ActivityTracker tracker, ILogger<ActivityRecorder> logger)
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task Touch(Guid account)
    {
        var day = Today;
        if (tracker.Seen(account, day)) return;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "AccountActivity" ("AccountId", "Day") VALUES ({account}, {day}) ON CONFLICT DO NOTHING""");
            tracker.Mark(account, day);
        }
        catch (Exception ex) { logger.LogWarning("Activity recording failed: {Type}", ex.GetType().Name); }
    }

    /// <summary>Counts one transition by <paramref name="actor"/>; demo, suspended and test-profile actors are skipped.</summary>
    public async Task Count(string kind, Guid actor)
    {
        var day = Today;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ActivityCounters" ("Day", "Kind", "Count")
                SELECT {day}, {kind}, 1
                WHERE NOT EXISTS (SELECT 1 FROM "Accounts" a LEFT JOIN "Profiles" p ON p."Id" = a."Id"
                                  WHERE a."Id" = {actor} AND (a."Demo" OR a."Suspended" OR COALESCE(p."TestProfile", FALSE)))
                ON CONFLICT ("Day", "Kind") DO UPDATE SET "Count" = "ActivityCounters"."Count" + 1
                """);
        }
        catch (Exception ex) { logger.LogWarning("Activity counting failed: {Type}", ex.GetType().Name); }
    }
}
