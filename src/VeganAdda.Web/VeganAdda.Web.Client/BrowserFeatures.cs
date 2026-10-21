using VeganAdda.Contracts;

namespace VeganAdda.Web.Client;

public sealed class BrowserFeatures(BrowserApi transport) : IMessagingApi, ISavedApi, IProfilesApi
{
    public Task<Inbox> GetInbox(int chatPage = 1, int requestPage = 1, string? key = null)
        => transport.Get<Inbox>($"inbox?chatPage={chatPage}&requestPage={requestPage}&key={Uri.EscapeDataString(key ?? "")}");
    public Task<SavedItems> GetSaved(int peoplePage = 1, int eventsPage = 1)
        => transport.Get<SavedItems>($"saved?peoplePage={peoplePage}&eventsPage={eventsPage}");
    public Task<ProfileInput> GetProfile() => transport.Get<ProfileInput>("profile");
    public Task<Person> GetPerson(Guid id) => transport.Get<Person>($"people/{id}");
    public Task<Ok> SaveProfile(ProfileInput input) => transport.Send<Ok>("profile", input);
    public Task<Page<Person>> SearchPeople(DiscoveryInput input)
    {
        if (input.Latitude.HasValue || input.Longitude.HasValue || input.RadiusKm.HasValue)
            return transport.Send<Page<Person>>("people/search", input);
        var query = new Dictionary<string, string?>
        {
            ["event"] = input.Event,
            ["checkedIn"] = input.CheckedIn.ToString().ToLowerInvariant(),
            ["country"] = input.Country,
            ["state"] = input.State,
            ["city"] = input.City,
            ["search"] = input.Search,
            ["interest"] = input.Interest,
            ["intent"] = input.Intent,
            ["verifiedVegansOnly"] = input.VerifiedVegansOnly.ToString().ToLowerInvariant(),
            ["includeNonVegan"] = input.IncludeNonVegan.ToString().ToLowerInvariant(),
            ["minAge"] = input.MinAge?.ToString(),
            ["maxAge"] = input.MaxAge?.ToString(),
            ["page"] = input.Page.ToString()
        };
        return transport.Get<Page<Person>>("people?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value ?? ""))));
    }
}
