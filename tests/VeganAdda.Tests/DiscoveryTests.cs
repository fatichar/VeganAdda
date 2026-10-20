using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class DiscoveryTests(DatabaseFixture f)
{
    private readonly string marker = Guid.NewGuid().ToString("N");
    private Task<object> Search(Guid? viewer, DiscoveryInput input) { input.Search = marker; return f.Call(viewer, "people/search", input, "POST"); }
    private async Task<Guid> Located(string country, string state, string city, double? latitude = null, double? longitude = null, int? age = null)
    {
        var id = await f.Member();
        await using var db = f.Db(); var p = (await db.Profiles.FindAsync(id))!;
        p.Country = country; p.State = state; p.City = city; p.Latitude = latitude; p.Longitude = longitude; p.Bio = marker;
        p.BirthDate = age.HasValue ? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-age.Value) : null;
        await db.SaveChangesAsync(); return id;
    }

    [Fact]
    public async Task Location_filters_are_optional_scoped_and_case_insensitive()
    {
        var city = "City-" + Guid.NewGuid().ToString("N");
        var first = await Located("India", "Region A", city);
        var second = await Located("Canada", "Region A", city);
        var third = await Located("India", "Region B", city);
        var viewer = await f.Member();
        var all = Assert.IsType<Page<Person>>(await Search(viewer, new() { City = city.ToLowerInvariant() }));
        Assert.Equal(3, all.Total);
        var country = Assert.IsType<Page<Person>>(await Search(viewer, new() { Country = "india", City = city }));
        Assert.Equal(2, country.Total);
        var scoped = Assert.IsType<Page<Person>>(await Search(viewer, new() { Country = "India", State = "region a", City = city }));
        Assert.Equal(first, Assert.Single(scoped.Items).Id);
        var stateOnly = Assert.IsType<Page<Person>>(await Search(viewer, new() { State = "Region B", City = city }));
        Assert.Equal(third, Assert.Single(stateOnly.Items).Id);
        Assert.DoesNotContain(country.Items, p => p.Id == second);
    }

    [Fact]
    public async Task Radius_filters_before_pagination_excludes_missing_coordinates_and_never_updates_profile()
    {
        var viewer = await Located("India", "Maharashtra", "Pune", 19, 74);
        var inside = await Located("India", "Maharashtra", "Pune", 18.52, 73.85);
        var outside = await Located("India", "Maharashtra", "Mumbai", 19.07, 72.87);
        var unknown = await Located("India", "Maharashtra", "Pune");
        var page = Assert.IsType<Page<Person>>(await Search(viewer, new() { Latitude = 18.52, Longitude = 73.85, RadiusKm = 25 }));
        Assert.Contains(page.Items, p => p.Id == inside); Assert.DoesNotContain(page.Items, p => p.Id == outside || p.Id == unknown);
        Assert.Equal(page.Items.Count, page.Total);
        var json = JsonSerializer.Serialize(page, Wire.Json);
        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("longitude", json, StringComparison.OrdinalIgnoreCase);
        var own = Assert.IsType<ProfileInput>(await f.Call(viewer, "profile"));
        Assert.Equal(19d, own.Latitude); Assert.Equal(74d, own.Longitude); Assert.Equal("Pune", own.City);
    }

    [Theory]
    [InlineData(0, 179.95, 0, -179.95, 20)]
    [InlineData(89.99, 0, 89.99, 180, 5)]
    [InlineData(0, 0, 0, 0, 1)]
    public async Task Radius_handles_antimeridian_poles_and_zero_coordinates(double lat, double lon, double targetLat, double targetLon, int radius)
    {
        var viewer = await f.Member();
        var target = await Located("Test", "Test", "Test", targetLat, targetLon);
        var page = Assert.IsType<Page<Person>>(await Search(viewer, new() { Latitude = lat, Longitude = lon, RadiusKm = radius }));
        Assert.Contains(page.Items, p => p.Id == target);
    }

    [Fact]
    public async Task Radius_preserves_blocks_visibility_vegan_filters_and_mutual_dating()
    {
        var viewer = await f.Member(dating: true);
        var blocked = await Located("Test", "Test", "Test", 0, 0);
        var nonvegan = await Located("Test", "Test", "Test", 0, 0);
        var unapproved = await Located("Test", "Test", "Test", 0, 0);
        var dater = await Located("Test", "Test", "Test", 0, 0);
        await using (var db = f.Db())
        {
            db.Blocks.Add(new() { Owner = blocked, Subject = viewer });
            (await db.Profiles.FindAsync(nonvegan))!.IsVegan = false;
            (await db.Profiles.FindAsync(unapproved))!.Approved = false;
            (await db.Profiles.FindAsync(dater))!.LookingFor = ["Dating"];
            await db.SaveChangesAsync();
        }
        var input = new DiscoveryInput { Latitude = 0, Longitude = 0, RadiusKm = 1 };
        var page = Assert.IsType<Page<Person>>(await Search(viewer, input));
        Assert.DoesNotContain(page.Items, p => p.Id == blocked || p.Id == nonvegan || p.Id == unapproved);
        input.IncludeNonVegan = true;
        Assert.Contains(Assert.IsType<Page<Person>>(await Search(viewer, input)).Items, p => p.Id == nonvegan);
        input.Intent = "Dating";
        Assert.Equal(dater, Assert.Single(Assert.IsType<Page<Person>>(await Search(viewer, input)).Items).Id);
        var ordinary = await f.Member();
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Search(ordinary, input))).Status);
    }

    [Theory]
    [InlineData(null, 0d, 25)]
    [InlineData(0d, null, 25)]
    [InlineData(91d, 0d, 25)]
    [InlineData(0d, 181d, 25)]
    [InlineData(0d, 0d, null)]
    [InlineData(0d, 0d, 0)]
    [InlineData(0d, 0d, 501)]
    public async Task Invalid_nearby_search_is_rejected(double? lat, double? lon, int? radius)
    {
        var viewer = await f.Member();
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => Search(viewer, new() { Latitude = lat, Longitude = lon, RadiusKm = radius }))).Status);
    }

    [Fact]
    public async Task Age_range_uses_birthdays_excludes_unknown_age_and_keeps_birth_date_private()
    {
        var viewer = await f.Member(); var city = Guid.NewGuid().ToString();
        var lower = await Located("Test", "Test", city, age: 25);
        var upper = await Located("Test", "Test", city, age: 30);
        var unknown = await Located("Test", "Test", city);
        var older = await Located("Test", "Test", city, age: 31);
        var birthdayTomorrow = await Located("Test", "Test", city, age: 25);
        await using (var db = f.Db()) { (await db.Profiles.FindAsync(birthdayTomorrow))!.BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25).AddDays(1); await db.SaveChangesAsync(); }
        var input = new DiscoveryInput { City = city, MinAge = 25, MaxAge = 30 };
        var result = Assert.IsType<Page<Person>>(await Search(viewer, input));
        Assert.Equal(2, result.Total); Assert.Contains(result.Items, p => p.Id == lower && p.Age == 25); Assert.Contains(result.Items, p => p.Id == upper && p.Age == 30);
        Assert.DoesNotContain(result.Items, p => p.Id == unknown || p.Id == older || p.Id == birthdayTomorrow);
        Assert.DoesNotContain("birthDate", JsonSerializer.Serialize(result, Wire.Json), StringComparison.OrdinalIgnoreCase);
        input.MinAge = 30; input.MaxAge = 25;
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => Search(viewer, input))).Status);
        input.MinAge = 17; input.MaxAge = null;
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => Search(viewer, input))).Status);
    }

    [Fact]
    public async Task Nearby_and_age_filters_require_sign_in()
    {
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Search(null, new() { Latitude = 0, Longitude = 0, RadiusKm = 25 }))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Search(null, new() { MinAge = 18 }))).Status);
    }

    [Fact]
    public async Task Radius_and_age_apply_before_counting_and_pagination()
    {
        var viewer = await f.Member();
        for (var i = 0; i < 15; i++) await Located("Test", "Test", "Test", 0, 0, 28);
        await Located("Test", "Test", "Test", 5, 5, 28);
        await Located("Test", "Test", "Test", 0, 0, 50);
        var input = new DiscoveryInput { Latitude = 0, Longitude = 0, RadiusKm = 5, MinAge = 25, MaxAge = 30 };
        var first = Assert.IsType<Page<Person>>(await Search(viewer, input));
        input.Page = 2;
        var second = Assert.IsType<Page<Person>>(await Search(viewer, input));
        Assert.Equal(15, first.Total); Assert.Equal(15, second.Total);
        Assert.Equal(12, first.Items.Count); Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(p => p.Id).Intersect(second.Items.Select(p => p.Id)));
    }

    [Fact]
    public async Task Birth_month_and_year_are_required_and_reject_underage_values()
    {
        var id = await f.Member(); var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        input.BirthYear = null;
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", input, "POST"))).Status);
        input.BirthYear = DateTime.UtcNow.Year - 17;
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", input, "POST"))).Status);
        input.BirthYear = DateTime.UtcNow.Year - 28; input.BirthMonth = 1; input.BirthDay = null;
        await f.Call(id, "profile", input, "POST");
        var saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Equal(input.BirthYear, saved.BirthYear); Assert.Equal(1, saved.BirthMonth); Assert.Null(saved.BirthDay);
    }
}
