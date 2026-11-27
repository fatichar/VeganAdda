using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Client;

// Hands reads made during prerendering to the WebAssembly render, so hydration starts from the
// same data the server rendered instead of blanking screens while it refetches. The page is
// served no-store and holds only responses the same signed-in browser would fetch anyway.
// Keys are API read paths; the server records a result per call and the browser takes them
// in order. Leftovers are discarded after the first interactive render so later refreshes
// always reach the network.
public sealed class PrerenderedReads
{
    private const string StateKey = "veganadda.prerendered-reads";
    private readonly Dictionary<string, Queue<string>> reads = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private bool sealedForReplay;

    private PrerenderedReads() { }

    // Server prerender: record successful reads and write them into the page.
    public static PrerenderedReads Recording(PersistentComponentState state)
    {
        var cache = new PrerenderedReads { sealedForReplay = true };
        state.RegisterOnPersisting(() =>
        {
            lock (cache.gate)
            {
                if (cache.reads.Count > 0)
                    state.PersistAsJson(StateKey, cache.reads.ToDictionary(item => item.Key, item => item.Value.ToList(), StringComparer.Ordinal));
            }
            return Task.CompletedTask;
        }, RenderMode.InteractiveWebAssembly);
        return cache;
    }

    // Browser: take what the server persisted, if this load followed a prerender.
    public static PrerenderedReads Replaying(PersistentComponentState state)
    {
        var cache = new PrerenderedReads();
        if (state.TryTakeFromJson<Dictionary<string, List<string>>>(StateKey, out var restored) && restored != null)
            foreach (var (key, values) in restored) cache.reads[key] = new Queue<string>(values);
        return cache;
    }

    public async Task<T> Record<T>(string? key, Func<Task<T>> read)
    {
        var result = await read();
        if (key != null)
        {
            var json = JsonSerializer.Serialize(result, Wire.Json);
            lock (gate)
            {
                if (!reads.TryGetValue(key, out var values)) reads[key] = values = new Queue<string>();
                values.Enqueue(json);
            }
        }
        return result;
    }

    public Task<T> Replay<T>(string? key, Func<Task<T>> read)
    {
        lock (gate)
        {
            if (!sealedForReplay && key != null && reads.TryGetValue(key, out var values) && values.TryDequeue(out var json))
                return Task.FromResult(JsonSerializer.Deserialize<T>(json, Wire.Json)!);
        }
        return read();
    }

    public void Seal()
    {
        lock (gate) { sealedForReplay = true; reads.Clear(); }
    }
}

// GET paths shared by the browser transport and the prerender adapter, so recorded reads match.
public static class ReadRoutes
{
    public const string Profile = "profile";
    public const string Phone = "profile/phone";
    public const string Verification = "verification";
    public const string ProfileReminderBatches = "admin/profile-reminders";
    public static string ProfileReminderBatch(Guid id) => $"admin/profile-reminders/{id}";
    public static string Schedule(string country, string state, string city, int page)
        => $"events/schedule?country={Uri.EscapeDataString(country)}&state={Uri.EscapeDataString(state)}&city={Uri.EscapeDataString(city)}&page={page}";
    public static string EventContacts(string search) => "events/contacts?search=" + Uri.EscapeDataString(search);
    public static string ProfileEvents(Guid personId, int page) => $"people/{personId}/events?page={page}";
    public static string Referrers(string search) => "verification/referrers?search=" + Uri.EscapeDataString(search);
    public static string ReferralCards(Guid personId) => $"verification/referrals/{personId}";
    public static string Inbox(int chatPage, int requestPage, string? key)
        => $"inbox?chatPage={chatPage}&requestPage={requestPage}&key={Uri.EscapeDataString(key ?? "")}";
    public static string Saved(int peoplePage, int eventsPage) => $"saved?peoplePage={peoplePage}&eventsPage={eventsPage}";
    public static string Stats(int days) => $"admin/stats?days={days}";
    public static string Person(Guid id) => $"people/{id}";

    // Null when the search needs coordinates, which are posted rather than put in a URL.
    public static string? People(DiscoveryInput input)
    {
        if (input.Latitude.HasValue || input.Longitude.HasValue || input.RadiusKm.HasValue) return null;
        var query = new Dictionary<string, string?>
        {
            ["sort"] = input.Sort,
            ["connectionsOnly"] = input.ConnectionsOnly.ToString().ToLowerInvariant(),
            ["event"] = input.Event,
            ["checkedIn"] = input.CheckedIn.ToString().ToLowerInvariant(),
            ["country"] = input.Country,
            ["state"] = input.State,
            ["city"] = input.City,
            ["search"] = input.Search,
            ["interest"] = input.Interest,
            ["gender"] = string.Join(",", input.Genders),
            ["intent"] = input.Intent,
            ["verifiedVegansOnly"] = input.VerifiedVegansOnly.ToString().ToLowerInvariant(),
            ["includeNonVegan"] = input.IncludeNonVegan.ToString().ToLowerInvariant(),
            ["minAge"] = input.MinAge?.ToString(),
            ["maxAge"] = input.MaxAge?.ToString(),
            ["page"] = input.Page.ToString()
        };
        return "people?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value ?? "")));
    }
}
