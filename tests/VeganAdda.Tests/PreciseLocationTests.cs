using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeganAdda.Contracts;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class PreciseLocationTests(DatabaseFixture f)
{
    [Fact]
    public async Task Migration_backfills_city_centers_and_preserves_existing_precise_locations()
    {
        var isolated = new DatabaseFixture();
        await isolated.InitializeAt("ProfilePhotoFocus");
        try
        {
            var approximate = await isolated.Member(); var precise = await isolated.Member();
            await using var db = isolated.Db(); var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261022104118_MultipleVerificationReferrers");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Profiles\" SET \"Latitude\" = {18.1}, \"Longitude\" = {73.1} WHERE \"Id\" = {precise}");
            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
            var a = (await db.Profiles.FindAsync(approximate))!;
            var b = (await db.Profiles.FindAsync(precise))!;
            var center = VeganAdda.Web.Features.Profiles.CityCenterCatalog.Find(a.Country, a.State, a.City)!;
            Assert.False(a.HasPreciseLocation); Assert.Equal(center.Latitude, a.Latitude); Assert.Equal(center.Longitude, a.Longitude);
            Assert.True(b.HasPreciseLocation); Assert.Equal(18.1, b.Latitude); Assert.Equal(73.1, b.Longitude);
        }
        finally { await isolated.DisposeAsync(); }
    }
    [Fact]
    public async Task Coordinates_require_opt_in_remain_private_and_can_be_removed()
    {
        var id = await f.Member();
        var viewer = await f.Member();
        var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.False(input.SavePreciseLocation);
        input.Latitude = 18.5204; input.Longitude = 73.8567;
        await f.Call(id, "profile", input, "POST");
        var saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Null(saved.Latitude); Assert.Null(saved.Longitude);

        input.SavePreciseLocation = true;
        await f.Call(id, "profile", input, "POST");
        saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.True(saved.SavePreciseLocation);
        Assert.Equal(input.Latitude, saved.Latitude); Assert.Equal(input.Longitude, saved.Longitude);
        Assert.True(saved.VeganVerified); // Private coordinate changes do not require profile review.
        foreach (var audience in new Guid?[] { null, viewer })
        {
            if (audience == null) Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => f.Call(audience, "people/" + id))).Status);
            else
            {
                var person = JsonSerializer.Serialize(await f.Call(audience, "people/" + id), Wire.Json);
                Assert.DoesNotContain("latitude", person, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("longitude", person, StringComparison.OrdinalIgnoreCase);
            }
            var people = JsonSerializer.Serialize(await f.Call(audience, "people"), Wire.Json);
            Assert.DoesNotContain("latitude", people, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("longitude", people, StringComparison.OrdinalIgnoreCase);
        }
        var admin = await f.Member(admin: true);
        await using (var db = f.Db())
        {
            (await db.Profiles.FindAsync(id))!.VeganVerified = false;
            await db.SaveChangesAsync();
        }
        var review = JsonSerializer.Serialize(await f.Call(admin, "moderation"), Wire.Json);
        Assert.DoesNotContain("latitude", review, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", review, StringComparison.OrdinalIgnoreCase);

        saved.SavePreciseLocation = false; // Even if a client submits old coordinates, discard them.
        await f.Call(id, "profile", saved, "POST");
        await using var stored = f.Db();
        var profile = await stored.Profiles.SingleAsync(p => p.Id == id);
        Assert.False(profile.HasPreciseLocation);
        var center = VeganAdda.Web.Features.Profiles.CityCenterCatalog.Find(profile.Country, profile.State, profile.City);
        Assert.NotNull(center);
        Assert.Equal(center.Latitude, profile.Latitude); Assert.Equal(center.Longitude, profile.Longitude);
    }

    [Fact]
    public async Task City_center_is_used_without_opt_in_and_follows_city_changes()
    {
        var id = await f.Member();
        var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        input.Country = "India"; input.State = "Maharashtra"; input.City = "Pune";
        await f.Call(id, "profile", input, "POST");
        await using (var db = f.Db())
        {
            var p = (await db.Profiles.FindAsync(id))!;
            var center = VeganAdda.Web.Features.Profiles.CityCenterCatalog.Find(input.Country, input.State, input.City)!;
            Assert.False(p.HasPreciseLocation); Assert.Equal(center.Latitude, p.Latitude); Assert.Equal(center.Longitude, p.Longitude);
        }
        input.City = "Unknown custom city";
        await f.Call(id, "profile", input, "POST");
        await using var stored = f.Db(); var profile = (await stored.Profiles.FindAsync(id))!;
        Assert.Null(profile.Latitude); Assert.Null(profile.Longitude); Assert.False(profile.HasPreciseLocation);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(18.5, null)]
    [InlineData(null, 73.8)]
    [InlineData(-90.1, 73.8)]
    [InlineData(90.1, 73.8)]
    [InlineData(18.5, -180.1)]
    [InlineData(18.5, 180.1)]
    public async Task Invalid_or_incomplete_coordinate_pairs_are_rejected(double? latitude, double? longitude)
    {
        var id = await f.Member();
        var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        input.SavePreciseLocation = true; input.Latitude = latitude; input.Longitude = longitude;
        var error = await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", input, "POST"));
        Assert.Equal(400, error.Status);
        var saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Null(saved.Latitude); Assert.Null(saved.Longitude);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    public async Task Valid_boundary_coordinates_round_trip(double latitude, double longitude)
    {
        var id = await f.Member();
        var input = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        input.SavePreciseLocation = true; input.Latitude = latitude; input.Longitude = longitude;
        await f.Call(id, "profile", input, "POST");
        var saved = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        Assert.Equal(latitude, saved.Latitude); Assert.Equal(longitude, saved.Longitude);
    }
}
