using VeganAdda.Contracts;
using VeganAdda.Web.Features.Discovery;
using VeganAdda.Web.Features.Messaging;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Saved;

namespace VeganAdda.Web.Services;

// Each overlapping prerender operation has an independent DbContext. Typed results
// need no serialization round-trip through the path-based compatibility adapter.
public sealed class ServerFeatures(IServiceScopeFactory scopes) : IMessagingApi, ISavedApi, IProfilesApi
{
    private async Task<T> Call<TService, T>(Func<TService, Task<T>> operation) where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }
    public Task<Inbox> GetInbox(int chatPage = 1, int requestPage = 1, string? key = null)
        => Call<InboxService, Inbox>(service => service.GetInbox(chatPage, requestPage, key));
    public Task<SavedItems> GetSaved(int peoplePage = 1, int eventsPage = 1)
        => Call<SavedService, SavedItems>(service => service.GetSaved(peoplePage, eventsPage));
    public Task<ProfileInput> GetProfile() => Call<ProfilesService, ProfileInput>(service => service.GetProfile());
    public Task<Person> GetPerson(Guid id) => Call<ProfilesService, Person>(service => service.GetPerson(id));
    public Task<Page<Person>> SearchPeople(DiscoveryInput input) => Call<DiscoveryService, Page<Person>>(service => service.SearchPeople(input));
    public Task<Ok> SaveProfile(ProfileInput input) => Call<ProfilesService, Ok>(service => service.SaveProfile(input));
}
