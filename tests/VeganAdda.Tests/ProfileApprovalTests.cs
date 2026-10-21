using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class ProfileApprovalTests(DatabaseFixture f)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Vegan_verification_matches_review_queue_independently_of_referrals(bool approved, bool referred)
    {
        var admin = await f.Member(admin: true);
        var member = await f.Member();
        await using (var db = f.Db())
        {
            if (referred) db.Referrals.Add(new() { ReferrerId = admin, ReferredId = member });
            (await db.Profiles.FindAsync(member))!.VeganVerified = approved;
            await db.SaveChangesAsync();
        }

        var data = Assert.IsType<AdminData>(await f.Call(admin, "admin"));
        var row = Assert.Single((await AllMembers(admin)).People, p => p.Id == member);
        Assert.True(row.HasProfile);
        Assert.Equal(approved, row.VeganVerified);
        Assert.False(await IsInReviewQueue(admin, member));
    }

    [Theory]
    [InlineData("admin/profile-review")]
    [InlineData("admin/moderate")]
    public async Task Confirming_and_removing_verification_updates_queue_but_keeps_discovery(string path)
    {
        var admin = await f.Member(admin: true);
        var member = await f.Member();
        var city = "Approval-" + Guid.NewGuid().ToString("N");
        await using (var db = f.Db())
        {
            var profile = (await db.Profiles.FindAsync(member))!;
            profile.VeganVerified = false;
            profile.City = city;
            profile.IsVegan = true;
            await db.SaveChangesAsync();
        }

        object approval = path == "admin/profile-review" ? new VeganVerificationInput(member, true) : new ModerateInput(member, "confirm-vegan");
        await f.Call(admin, path, approval, "POST");
        var data = await AllMembers(admin);
        Assert.True(Assert.Single(data.People, p => p.Id == member).VeganVerified);
        Assert.False(await IsInReviewQueue(admin, member));
        Assert.False(Assert.IsType<ProfileInput>(await f.Call(member, "profile")).VeganStatusUnverified);
        Assert.Contains(Assert.IsType<Page<Person>>(await f.Call(null, "people?city=" + city)).Items, p => p.Id == member);

        await using (var db = f.Db())
        {
            (await db.Accounts.FindAsync(member))!.Moderator = true;
            (await db.Profiles.FindAsync(member))!.Featured = true;
            await db.SaveChangesAsync();
        }
        await f.Call(admin, "admin/moderate", new ModerateInput(member, "remove-vegan-verification"), "POST");
        data = await AllMembers(admin);
        var revoked = Assert.Single(data.People, p => p.Id == member);
        Assert.False(revoked.VeganVerified);
        Assert.True(revoked.Featured);
        Assert.False(revoked.Moderator);
        Assert.False(await IsInReviewQueue(admin, member));
        Assert.Contains(Assert.IsType<Page<Person>>(await f.Call(null, "people?city=" + city)).Items, p => p.Id == member);

        await f.Call(admin, "admin/moderate", new ModerateInput(member, "confirm-vegan"), "POST");
        data = await AllMembers(admin);
        Assert.True(Assert.Single(data.People, p => p.Id == member).VeganVerified);
        Assert.False(await IsInReviewQueue(admin, member));
    }

    [Theory]
    [InlineData("confirm-vegan")]
    [InlineData("remove-vegan-verification")]
    public async Task Verification_actions_require_an_administrator(string action)
    {
        var member = await f.Member();
        var target = await f.Member();
        var error = await Assert.ThrowsAsync<ApiException>(() => f.Call(member, "admin/moderate", new ModerateInput(target, action), "POST"));
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Administrator_role_changes_are_notified_and_cannot_remove_the_last_admin()
    {
        var admin = await f.Member(admin: true);
        var member = await f.Member();
        await using (var db = f.Db())
        {
            // Keep both role-change targets beyond the first member page, even in isolation.
            for (var i = 0; i < 20; i++)
                db.Accounts.Add(new Account { Name = $"AAA role pagination {i:00}", Subject = Guid.NewGuid().ToString() });
            await db.SaveChangesAsync();
        }

        await f.Call(admin, "admin/moderate", new ModerateInput(member, "grant-admin"), "POST");
        var data = await AllMembers(admin);
        Assert.True(Assert.Single(data.People, p => p.Id == member).Admin);
        var appointed = Assert.Single(Assert.IsType<NotificationList>(await f.Call(member, "notifications")).Items);
        Assert.Equal("admin-appointed", appointed.Kind);
        Assert.Contains("appointed", appointed.Text);
        Assert.Equal("/admin", appointed.Url);

        await f.Call(member, "admin/moderate", new ModerateInput(admin, "revoke-admin"), "POST");
        data = await AllMembers(member);
        Assert.False(Assert.Single(data.People, p => p.Id == admin).Admin);
        var revoked = Assert.IsType<NotificationList>(await f.Call(admin, "notifications"));
        Assert.Equal("admin-revoked", Assert.Single(revoked.Items).Kind);
        Assert.Equal("/", revoked.Items[0].Url);

        var error = await Assert.ThrowsAsync<ApiException>(() => f.Call(member, "admin/moderate", new ModerateInput(member, "revoke-admin"), "POST"));
        Assert.Equal(409, error.Status);
    }

    private async Task<AdminData> AllMembers(Guid admin)
    {
        var first = Assert.IsType<AdminData>(await f.Call(admin, "admin?section=Members&page=1"));
        var people = first.People.ToList();
        var pages = (first.MembersTotal + first.PageSize - 1) / first.PageSize;
        for (var page = 2; page <= pages; page++)
        {
            var next = Assert.IsType<AdminData>(await f.Call(admin, $"admin?section=Members&page={page}"));
            people.AddRange(next.People);
        }
        return first with { People = people };
    }

    private async Task<bool> IsInReviewQueue(Guid admin, Guid profileId)
    {
        var first = Assert.IsType<ModerationData>(await f.Call(admin, "moderation?section=Profiles&page=1"));
        if (first.Profiles.Any(p => p.Id == profileId)) return true;
        var pages = (first.ProfilesTotal + first.PageSize - 1) / first.PageSize;
        for (var page = 2; page <= pages; page++)
        {
            var next = Assert.IsType<ModerationData>(await f.Call(admin, $"moderation?section=Profiles&page={page}"));
            if (next.Profiles.Any(p => p.Id == profileId)) return true;
        }
        return false;
    }
}
