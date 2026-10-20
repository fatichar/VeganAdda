using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class PreciseLocationTests(DatabaseFixture f)
{
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
        Assert.True(saved.Approved); // Private coordinate changes do not require profile review.
        foreach (var audience in new Guid?[] { null, viewer })
        {
            var person = JsonSerializer.Serialize(await f.Call(audience, "people/" + id), Wire.Json);
            Assert.DoesNotContain("latitude", person, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("longitude", person, StringComparison.OrdinalIgnoreCase);
            var people = JsonSerializer.Serialize(await f.Call(audience, "people"), Wire.Json);
            Assert.DoesNotContain("latitude", people, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("longitude", people, StringComparison.OrdinalIgnoreCase);
        }
        var admin = await f.Member(admin: true);
        await using (var db = f.Db())
        {
            (await db.Profiles.FindAsync(id))!.Approved = false;
            await db.SaveChangesAsync();
        }
        var review = JsonSerializer.Serialize(await f.Call(admin, "moderation"), Wire.Json);
        Assert.DoesNotContain("latitude", review, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", review, StringComparison.OrdinalIgnoreCase);

        saved.SavePreciseLocation = false; // Even if a client submits old coordinates, discard them.
        await f.Call(id, "profile", saved, "POST");
        await using var stored = f.Db();
        var profile = await stored.Profiles.SingleAsync(p => p.Id == id);
        Assert.Null(profile.Latitude); Assert.Null(profile.Longitude);
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
