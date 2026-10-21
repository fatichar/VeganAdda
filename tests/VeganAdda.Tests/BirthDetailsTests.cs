using System.Text.Json;
using VeganAdda.Contracts;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class BirthDetailsTests(DatabaseFixture f)
{
    [Theory]
    [InlineData(null, 2000, null)]
    [InlineData(1, null, null)]
    [InlineData(null, null, null)]
    [InlineData(0, 2000, null)]
    [InlineData(13, 2000, null)]
    [InlineData(2, 2001, 29)]
    [InlineData(4, 2000, 31)]
    [InlineData(1, 2000, 0)]
    [InlineData(1, 2000, 32)]
    public async Task Missing_or_invalid_birth_details_are_rejected(int? month, int? year, int? day)
    {
        var id = await f.Member(); var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        input.BirthMonth = month; input.BirthYear = year; input.BirthDay = day;
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", input, "POST"))).Status);
    }

    [Fact]
    public async Task Month_only_birth_details_round_trip_without_inventing_a_birthday()
    {
        var id = await f.Member(); var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        input.BirthYear = 2000; input.BirthMonth = 2; input.BirthDay = null;
        await f.Call(id, "profile", input, "POST");
        var saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Equal(2000, saved.BirthYear); Assert.Equal(2, saved.BirthMonth); Assert.Null(saved.BirthDay);
        await using var db = f.Db(); var stored = (await db.Profiles.FindAsync(id))!;
        Assert.False(stored.HasExactBirthDate); Assert.Equal(new DateOnly(2000, 2, 29), stored.BirthDate);
    }

    [Fact]
    public async Task Optional_day_can_be_added_and_removed_and_enables_birthday_highlight()
    {
        var id = await f.Member(); var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        input.BirthYear = today.Year - 28; input.BirthMonth = today.Month; input.BirthDay = today.Day;
        await f.Call(id, "profile", input, "POST");
        var saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Equal(today.Day, saved.BirthDay);
        var person = Assert.IsType<Person>(await f.Call(id, "people/" + id));
        Assert.True(person.IsBirthday); Assert.Equal(28, person.Age);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(person, Wire.Json));
        foreach (var field in new[] { "birthDate", "birthYear", "birthMonth", "birthDay", "hasExactBirthDate" })
            Assert.False(json.RootElement.TryGetProperty(field, out _));

        input.BirthDay = null;
        await f.Call(id, "profile", input, "POST");
        saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Null(saved.BirthDay);
        person = Assert.IsType<Person>(await f.Call(id, "people/" + id));
        Assert.False(person.IsBirthday);
        Assert.Equal(today.Day == DateTime.DaysInMonth(today.Year - 28, today.Month) ? 28 : 27, person.Age);
    }

    [Fact]
    public async Task Missing_day_never_creates_a_highlight_even_on_the_stored_surrogate_date()
    {
        var id = await f.Member(); var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = f.Db())
        {
            var p = (await db.Profiles.FindAsync(id))!;
            p.BirthDate = today.AddYears(-28); p.HasExactBirthDate = false;
            await db.SaveChangesAsync();
        }
        Assert.False(Assert.IsType<Person>(await f.Call(id, "people/" + id)).IsBirthday);
    }

    [Fact]
    public async Task Month_only_birth_details_are_used_by_discovery_age_filters()
    {
        var id = await f.Member(); var viewer = await f.Member();
        var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        var today = DateTime.UtcNow;
        input.BirthYear = today.Year - 28 - (today.Month == 1 ? 1 : 0); input.BirthMonth = today.Month == 1 ? 12 : today.Month - 1; input.BirthDay = null;
        input.Bio = "birth-month-filter-" + Guid.NewGuid().ToString("N");
        await f.Call(id, "profile", input, "POST");
        await using (var db = f.Db()) { (await db.Profiles.FindAsync(id))!.VeganVerified = true; await db.SaveChangesAsync(); }
        var page = Assert.IsType<Page<Person>>(await f.Call(viewer, "people?minAge=28&maxAge=28&search=" + input.Bio));
        Assert.Contains(page.Items, p => p.Id == id && p.Age == 28);
    }
}
