using System.Reflection;
using VeganAdda.Contracts;
using VeganAdda.UI.Pages;

namespace VeganAdda.Tests;

public class DiscoveryScrollTests
{
    [Fact]
    public async Task Failed_page_can_retry_without_skipping_and_refresh_replaces_the_feed()
    {
        var api = new PeopleApi();
        var screen = new Harness(api);
        var first = Person(); var second = Person();
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([first], 3, 1, 1)));
        await screen.Reload();
        api.Results.Enqueue(() => throw new HttpRequestException());
        await screen.More();
        Assert.Single(screen.People);
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([first, second], 3, 2, 1)));
        await screen.More();
        Assert.Equal(new[] { 1, 2, 2 }, api.Pages);
        Assert.Equal(new[] { first.Id, second.Id }, screen.People.Select(p => p.Id));
        await screen.ParentRerender();
        Assert.Equal(new[] { 1, 2, 2 }, api.Pages);
        Assert.Equal(2, screen.People.Count);
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([second], 1, 1, 1)));
        await screen.Reload();
        await screen.More();
        Assert.Equal(second.Id, Assert.Single(screen.People).Id);
        Assert.Equal(new[] { 1, 2, 2, 1 }, api.Pages);
    }

    [Fact]
    public async Task Old_page_response_cannot_append_to_new_filters_and_duplicate_loads_are_ignored()
    {
        var api = new PeopleApi(); var screen = new Harness(api);
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([Person()], 2, 1, 1)));
        await screen.Reload();
        var pending = new TaskCompletionSource<Page<Person>>();
        api.Results.Enqueue(() => pending.Task);
        var loading = screen.More();
        await screen.More();
        var replacement = Person();
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([replacement], 1, 1, 1)));
        await screen.Reload();
        pending.SetResult(new([Person()], 2, 2, 1));
        await loading;
        Assert.Equal(replacement.Id, Assert.Single(screen.People).Id);
        Assert.Equal(new[] { 1, 2, 1 }, api.Pages);
    }

    static Person Person() => new(Guid.NewGuid(), "Member", "Pune", "", "", [], [], null, null, null, false, false, "");
    sealed class Harness : Discover
    {
        public Harness(IProfilesApi api) => Profiles = api;
        public Task Reload() => Refresh();
        public Task More() => LoadMore();
        public Task ParentRerender()
        {
            typeof(Discover).GetField("locationReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, true);
            return OnParametersSetAsync();
        }
        public List<Person> People => (List<Person>)typeof(Discover).GetField("people", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
    }
    sealed class PeopleApi : IProfilesApi
    {
        public List<int> Pages = [];
        public Queue<Func<Task<Page<Person>>>> Results = new();
        public Task<Page<Person>> SearchPeople(DiscoveryInput input) { Pages.Add(input.Page); return Results.Dequeue()(); }
        public Task<ProfileInput> GetProfile() => throw new NotSupportedException();
        public Task<Person> GetPerson(Guid id) => throw new NotSupportedException();
        public Task<Ok> SaveProfile(ProfileInput input) => throw new NotSupportedException();
    }
}
