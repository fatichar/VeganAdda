using System.Reflection;
using VeganAdda.Contracts;
using VeganAdda.UI.Pages;

namespace VeganAdda.Tests;

public class DiscoveryScrollTests
{
    [Theory]
    [InlineData("name", DiscoverySort.Name, DiscoverySort.NameDescending)]
    [InlineData("age", DiscoverySort.Age, DiscoverySort.AgeDescending)]
    [InlineData("distance", DiscoverySort.Distance, DiscoverySort.DistanceDescending)]
    public async Task Explicit_order_changes_the_search_and_reselecting_the_field_keeps_that_order(string field, string ascending, string descending)
    {
        var api = new PeopleApi();
        var screen = new Harness(api);
        screen.SetSort(ascending);
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([], 0, 1, 20)));
        await screen.ChooseOrder(true);
        Assert.Equal(descending, Assert.Single(api.Sorts));
        await screen.ChooseField(field);
        await screen.ChooseOrder(true);
        Assert.Single(api.Sorts);
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([], 0, 1, 20)));
        await screen.ChooseOrder(false);
        Assert.Equal(new[] { descending, ascending }, api.Sorts);
    }

    [Theory]
    [InlineData("name", DiscoverySort.Name)]
    [InlineData("age", DiscoverySort.Age)]
    [InlineData("distance", DiscoverySort.Distance)]
    public async Task Switching_fields_starts_with_the_natural_order(string field, string expected)
    {
        var api = new PeopleApi();
        var screen = new Harness(api);
        screen.SetSort(field == "name" ? DiscoverySort.AgeDescending : DiscoverySort.NameDescending);
        api.Results.Enqueue(() => Task.FromResult(new Page<Person>([], 0, 1, 20)));
        await screen.ChooseField(field);
        Assert.Equal(expected, Assert.Single(api.Sorts));
    }

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
        public Harness(IProfilesApi api)
        {
            Profiles = api;
            typeof(Discover).GetField("locationReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, true);
        }
        public Task Reload() => Refresh();
        public Task More() => LoadMore();
        public void SetSort(string value)
        {
            typeof(Discover).GetField("sort", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, value);
            typeof(Discover).GetField("latitude", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, (double?)18.52);
            typeof(Discover).GetField("longitude", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, (double?)73.85);
        }
        public Task ChooseField(string field) => (Task)typeof(Discover).GetMethod("ChangeSortField", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, [field])!;
        public Task ChooseOrder(bool descending) => (Task)typeof(Discover).GetMethod("ChangeSortOrder", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, [descending])!;
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
        public List<string> Sorts = [];
        public Queue<Func<Task<Page<Person>>>> Results = new();
        public Task<Page<Person>> SearchPeople(DiscoveryInput input) { Pages.Add(input.Page); Sorts.Add(input.Sort); return Results.Dequeue()(); }
        public Task<ProfileInput> GetProfile() => throw new NotSupportedException();
        public Task<Person> GetPerson(Guid id) => throw new NotSupportedException();
        public Task<Ok> SaveProfile(ProfileInput input) => throw new NotSupportedException();
    }
}
