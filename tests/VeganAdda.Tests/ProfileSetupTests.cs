using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeganAdda.Contracts;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class ProfileSetupTests(DatabaseFixture f)
{
    [Theory]
    [InlineData("profile")]
    [InlineData("name")]
    [InlineData("vegan")]
    [InlineData("birth")]
    [InlineData("country")]
    [InlineData("state")]
    [InlineData("city")]
    [InlineData("adult")]
    public async Task Incomplete_members_can_finish_setup_but_cannot_use_community_apis(string missing)
    {
        var id = await f.Member();
        var target = await f.Member();
        await using (var db = f.Db())
        {
            var p = (await db.Profiles.FindAsync(id))!;
            switch (missing)
            {
                case "profile": db.Profiles.Remove(p); break;
                case "name": p.Name = " "; break;
                case "vegan": p.IsVegan = null; break;
                case "birth": p.BirthDate = null; break;
                case "country": p.Country = " "; break;
                case "state": p.State = " "; break;
                case "city": p.City = " "; break;
                case "adult": p.AdultConfirmedAt = default; break;
            }
            await db.SaveChangesAsync();
        }
        Assert.False(Assert.IsType<Session>(await f.Call(id, "session")).BasicProfileComplete);
        foreach (var path in new[] { "people", "events", "connections", "saved", "notifications" })
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, path))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "connections", new ConnectionInput(target), "POST"))).Status);
        Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        await f.Call(id, "profile", new ProfileInput
        {
            Name = "Basic member", Gender = "Man", IsVegan = true, BirthYear = 1990, BirthMonth = 1,
            Country = "India", State = "Maharashtra", City = "Pune"
        }, "POST");
        Assert.True(Assert.IsType<Session>(await f.Call(id, "session")).BasicProfileComplete);
        Assert.IsType<Page<Person>>(await f.Call(id, "people"));
    }

    [Fact]
    public async Task Profile_save_cannot_grant_verification_and_vegan_changes_require_reconfirmation()
    {
        var admin = await f.Member(admin: true);
        var id = await f.Member();
        var p = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        p.IsVegan = false;
        await f.Call(id, "profile", p, "POST");
        Assert.False(Assert.IsType<ProfileInput>(await f.Call(id, "profile")).VeganVerified);
        p.IsVegan = true; p.VeganVerified = true;
        await f.Call(id, "profile", p, "POST");
        Assert.False(Assert.IsType<ProfileInput>(await f.Call(id, "profile")).VeganVerified);
        Assert.Equal(410, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "referrals/invite", null, "POST"))).Status);
        await f.Call(admin, "admin/profile-review", new VeganVerificationInput(id, true), "POST");
        Assert.True(Assert.IsType<ProfileInput>(await f.Call(id, "profile")).CanRefer);
    }

    [Fact]
    public async Task Consenting_does_not_feature_a_profile_and_private_profiles_cannot_be_featured()
    {
        var admin = await f.Member(admin: true);
        var id = await f.Member(pub: false);
        await using (var db = f.Db())
        {
            Assert.True((await db.Profiles.FindAsync(id))!.FeatureConsent);
        }
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(admin, "admin/moderate", new ModerateInput(id, "feature"), "POST"))).Status);
        var p = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        p.Public = true;
        await f.Call(id, "profile", p, "POST");
        Assert.False(Assert.IsType<Person>(await f.Call(null, "people/" + id)).Featured);
        await f.Call(admin, "admin/moderate", new ModerateInput(id, "feature"), "POST");
        Assert.True(Assert.IsType<Person>(await f.Call(null, "people/" + id)).Featured);
        p.Public = false;
        await f.Call(id, "profile", p, "POST");
        await using var check = f.Db();
        Assert.False((await check.Profiles.FindAsync(id))!.Featured);
    }

    [Fact]
    public async Task Verified_only_filter_precedes_count_and_pagination_and_excludes_nonvegans()
    {
        var viewer = await f.Member();
        var city = Guid.NewGuid().ToString("N");
        for (var i = 0; i < 15; i++)
        {
            var id = await f.Member();
            await using var db = f.Db();
            var profile = (await db.Profiles.FindAsync(id))!;
            profile.City = city;
            profile.VeganVerified = i != 13;
            if (i == 14) profile.IsVegan = false;
            await db.SaveChangesAsync();
        }
        var all = Assert.IsType<Page<Person>>(await f.Call(viewer, "people?city=" + city + "&includeNonVegan=true"));
        Assert.Equal(15, all.Total);
        var first = Assert.IsType<Page<Person>>(await f.Call(viewer, "people?city=" + city + "&includeNonVegan=true&verifiedVegansOnly=true"));
        var second = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput
        {
            City = city, IncludeNonVegan = true, VerifiedVegansOnly = true, Page = 2
        }, "POST"));
        Assert.Equal(13, first.Total);
        Assert.Equal(12, first.Items.Count);
        Assert.Single(second.Items);
        Assert.All(first.Items.Concat(second.Items), p => Assert.True(p.VeganVerified && p.IsVegan == true));
    }

    [Fact]
    public async Task Optional_profession_changes_preserve_verification_and_empty_values_are_allowed()
    {
        var id = await f.Member();
        var profile = Assert.IsType<ProfileInput>(await f.Call(id, "profile"));
        profile.Profession = "  Teacher  ";
        await f.Call(id, "profile", profile, "POST");
        var person = Assert.IsType<Person>(await f.Call(null, "people/" + id));
        Assert.Equal("Teacher", person.Profession);
        Assert.True(person.VeganVerified);
        profile.Profession = new string('x', 81);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", profile, "POST"))).Status);
        profile.Profession = " ";
        await f.Call(id, "profile", profile, "POST");
        Assert.Equal("", Assert.IsType<Person>(await f.Call(null, "people/" + id)).Profession);
    }

    [Fact]
    public async Task Migration_backfills_only_approved_declared_vegans_and_enables_all_feature_consent()
    {
        var isolated = new DatabaseFixture();
        await isolated.InitializeAsync();
        try
        {
            var cases = new (bool Approved, bool? Vegan)[] { (true, true), (true, false), (true, null), (false, true) };
            var ids = new List<Guid>();
            foreach (var _ in cases) ids.Add(await isolated.Member());
            await using var db = isolated.Db();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261021130000_PushDeliveryLeases");
            for (var i = 0; i < cases.Length; i++)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Profiles" SET "Approved" = {cases[i].Approved}, "IsVegan" = {cases[i].Vegan},
                        "FeatureConsent" = FALSE, "Featured" = FALSE WHERE "Id" = {ids[i]}
                    """);
            await migrator.MigrateAsync();
            for (var i = 0; i < cases.Length; i++)
            {
                var profile = (await db.Profiles.FindAsync(ids[i]))!;
                Assert.Equal(cases[i].Approved && cases[i].Vegan == true, profile.VeganVerified);
                Assert.Equal(cases[i].Vegan, profile.IsVegan);
                Assert.True(profile.FeatureConsent);
                Assert.False(profile.Featured);
            }
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("//example.com")]
    [InlineData("/\\example.com")]
    [InlineData("/onboarding?returnUrl=/onboarding")]
    [InlineData("/\nexample.com")]
    public void Unsafe_or_recursive_return_targets_fall_back_to_discover(string target)
        => Assert.Equal("/", ProfileSetup.SafeReturnUrl(target));

    [Fact]
    public void Authentication_preserves_invitation_codes_and_safe_return_targets()
    {
        const string invitation = "/onboarding?ref=abcdefghijklmnopqrst&returnUrl=%2Fevents";
        Assert.Equal(invitation, ProfileSetup.SignInDestination(false, invitation));
        Assert.Equal("/onboarding?returnUrl=%2Fevents", ProfileSetup.SignInDestination(false, "/events"));
        Assert.Equal("/events", ProfileSetup.SignInDestination(true, "/events"));
    }
}
