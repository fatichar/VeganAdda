using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeganAdda.Contracts;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class ProfileGenderTests(DatabaseFixture f)
{
    [Fact]
    public async Task Required_gender_round_trips_and_rejects_missing_or_unknown_values()
    {
        var id = await f.Member();
        var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Equal("Man", input.Gender);
        foreach (var gender in Wire.Genders)
        {
            input.Gender = gender;
            await f.Call(id, "profile", input, "POST");
            var saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
            Assert.Equal(gender, saved.Gender);
            Assert.True(saved.VeganVerified);
            Assert.True(Assert.IsType<Session>(await f.Call(id, "session")).BasicProfileComplete);
        }
        foreach (var invalid in new[] { "", " ", "unsupported" })
        {
            input.Gender = invalid;
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", input, "POST"))).Status);
        }
    }

    [Fact]
    public async Task Legacy_profiles_receive_one_gender_reminder_and_saving_marks_it_read()
    {
        var isolated = new DatabaseFixture();
        await isolated.InitializeAsync();
        try
        {
            var missing = await isolated.Member(); var assigned = await isolated.Member();
            await using (var db = isolated.Db())
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20261021170000_NormalizeProfileGender");
                (await db.Profiles.FindAsync(missing))!.Gender = "";
                await db.SaveChangesAsync();
                await migrator.MigrateAsync();
                await migrator.MigrateAsync();
                var notice = Assert.Single(await db.Notifications.Where(n => n.UserId == missing && n.Kind == "profile-gender-required").ToListAsync());
                Assert.False(notice.Read);
                Assert.Equal("/settings/profile", notice.Url);
                Assert.False(await db.Notifications.AnyAsync(n => n.UserId == assigned && n.Kind == "profile-gender-required"));
            }
            // Legacy members retain access while they update their profile.
            Assert.True(((Session)await isolated.Call(missing, "session")).BasicProfileComplete);
            var input = (ProfileInput)await isolated.Call(missing, "profile");
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => isolated.Call(missing, "profile", input, "POST"))).Status);
            input.Gender = "Woman";
            await isolated.Call(missing, "profile", input, "POST");
            await using var saved = isolated.Db();
            Assert.True((await saved.Notifications.SingleAsync(n => n.UserId == missing && n.Kind == "profile-gender-required")).Read);
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task Gender_filter_precedes_pagination_in_both_search_routes_and_preserves_visibility()
    {
        var viewer = await f.Member();
        var city = Guid.NewGuid().ToString("N");
        var expected = new HashSet<Guid>();
        for (var i = 0; i < 16; i++)
        {
            var id = await f.Member();
            var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
            input.City = city;
            input.Gender = i < 14 ? "Woman" : i == 14 ? "Man" : "Other";
            await f.Call(id, "profile", input, "POST");
            if (i == 15)
            {
                // Legacy blank genders remain searchable while members are prompted to update.
                await using var legacyDb = f.Db();
                (await legacyDb.Profiles.FindAsync(id))!.Gender = "";
                await legacyDb.SaveChangesAsync();
            }
            if (i < 13) expected.Add(id);
            if (i == 13) await f.Call(id, "people/" + viewer + "/block", null, "POST");
        }
        var first = Assert.IsType<Page<Person>>(await f.Call(viewer, "people?city=" + city + "&gender=Woman"));
        var second = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { City = city, Genders = ["Woman"], Page = 2 }, "POST"));
        Assert.Equal(13, first.Total);
        Assert.Equal(13, second.Total);
        Assert.Equal(12, first.Items.Count);
        Assert.Single(second.Items);
        Assert.True(expected.SetEquals(first.Items.Concat(second.Items).Select(p => p.Id)));
        await using var db = f.Db();
        expected.Add(await db.Profiles.Where(p => p.City == city && p.Gender == "Man").Select(p => p.Id).SingleAsync());
        var multiple = Assert.IsType<Page<Person>>(await f.Call(viewer, "people?city=" + city + "&gender=Woman,Man"));
        var multipleSecond = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { City = city, Genders = ["Woman", "Man"], Page = 2 }, "POST"));
        Assert.Equal(14, multiple.Total);
        Assert.Equal(14, multipleSecond.Total);
        Assert.True(expected.SetEquals(multiple.Items.Concat(multipleSecond.Items).Select(p => p.Id)));
        var unfiltered = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { City = city }, "POST"));
        Assert.Equal(15, unfiltered.Total);
        var unspecified = Assert.IsType<Page<Person>>(await f.Call(viewer, "people?city=" + city + "&gender=Unspecified"));
        Assert.Single(unspecified.Items);
        Assert.Equal(1, unspecified.Total);
        var withUnspecified = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { City = city, Genders = ["Man", "Unspecified"] }, "POST"));
        Assert.Equal(2, withUnspecified.Total);
        Assert.Contains(withUnspecified.Items, p => p.Id == unspecified.Items[0].Id);
        var allGenders = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { City = city, Genders = Wire.GenderFilters }, "POST"));
        Assert.Equal(unfiltered.Total, allGenders.Total);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(viewer, "people?gender=Woman,unsupported"))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(viewer, "people/search", new DiscoveryInput { Genders = ["Man", "unsupported"] }, "POST"))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, "people?gender=Woman"))).Status);
    }

    [Fact]
    public async Task Other_and_unspecified_are_distinct_and_legacy_genders_migrate_to_other()
    {
        var isolated = new DatabaseFixture();
        await isolated.InitializeAsync();
        try
        {
            var viewer = await isolated.Member();
            var ids = new List<Guid>();
            foreach (var _ in new[] { "Non-binary", "Another gender", "Other", "" }) ids.Add(await isolated.Member());
            await using var db = isolated.Db();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261021160000_ProfileGender");
            var values = new[] { "Non-binary", "Another gender", "Other", "" };
            for (var i = 0; i < ids.Count; i++)
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Profiles\" SET \"Gender\" = {values[i]} WHERE \"Id\" = {ids[i]}");
            await migrator.MigrateAsync();
            var others = Assert.IsType<Page<Person>>(await isolated.Call(viewer, "people?gender=Other"));
            Assert.Equal(3, others.Total);
            Assert.True(ids.Take(3).ToHashSet().SetEquals(others.Items.Select(p => p.Id)));
            var unspecified = Assert.IsType<Page<Person>>(await isolated.Call(viewer, "people?gender=Unspecified"));
            Assert.Equal(ids[3], Assert.Single(unspecified.Items).Id);
            foreach (var id in ids.Take(3))
                Assert.Equal("Other", Assert.IsType<ProfileInput>(await isolated.Call(id, "profile")).Gender);
        }
        finally { await isolated.DisposeAsync(); }
    }
}
