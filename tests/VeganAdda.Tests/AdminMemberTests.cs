using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class AdminMemberTests(DatabaseFixture f)
{
    [Fact]
    public async Task Approval_filters_separate_approved_pending_and_incomplete_members()
    {
        var admin = await f.Member(admin: true);
        var approved = await f.Member();
        var pending = await f.Member();
        var incomplete = new Account { Name = "Incomplete member", Subject = Guid.NewGuid().ToString() };
        await using (var db = f.Db())
        {
            (await db.Profiles.FindAsync(pending))!.Approved = false;
            db.Accounts.Add(incomplete);
            await db.SaveChangesAsync();
        }

        var approvedRows = Assert.IsType<AdminData>(await f.Call(admin, "admin?approval=approved")).People;
        Assert.Contains(approvedRows, p => p.Id == approved);
        Assert.All(approvedRows, p => Assert.True(p.Approved && p.HasProfile));
        var pendingRows = Assert.IsType<AdminData>(await f.Call(admin, "admin?approval=awaiting-approval")).People;
        Assert.Contains(pendingRows, p => p.Id == pending);
        Assert.All(pendingRows, p => Assert.True(!p.Approved && p.HasProfile));
        var incompleteRows = Assert.IsType<AdminData>(await f.Call(admin, "admin?approval=no-profile")).People;
        Assert.Contains(incompleteRows, p => p.Id == incomplete.Id);
        Assert.All(incompleteRows, p => Assert.False(p.HasProfile));
        var allRows = Assert.IsType<AdminData>(await f.Call(admin, "admin?approval=all")).People;
        Assert.Contains(allRows, p => p.Id == approved);
        Assert.Contains(allRows, p => p.Id == pending);
        Assert.Contains(allRows, p => p.Id == incomplete.Id);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(admin, "admin?approval=invalid"))).Status);
    }

    [Fact]
    public async Task Pending_filter_runs_before_the_member_limit()
    {
        var admin = await f.Member(admin: true);
        var pending = await f.Member();
        var fixtureIds = new List<Guid>();
        await using (var db = f.Db())
        {
            (await db.Accounts.FindAsync(pending))!.Name = "ZZZ pending member";
            (await db.Profiles.FindAsync(pending))!.Approved = false;
            for (var i = 0; i < 201; i++)
            {
                var account = new Account { Name = $"AAA approved {i:000}", Subject = Guid.NewGuid().ToString() };
                fixtureIds.Add(account.Id);
                db.Accounts.Add(account);
                db.Profiles.Add(new() { Id = account.Id, Name = account.Name, Approved = true, AdultConfirmedAt = DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        try
        {
            var data = Assert.IsType<AdminData>(await f.Call(admin, "admin?approval=awaiting-approval"));
            Assert.Contains(data.People, p => p.Id == pending);
        }
        finally
        {
            await using var db = f.Db();
            await db.Accounts.Where(a => fixtureIds.Contains(a.Id)).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Admins_can_open_full_profiles_without_exposing_pending_or_suspended_profiles(bool approved, bool suspended)
    {
        var admin = await f.Member(admin: true);
        var viewer = await f.Member();
        var member = await f.Member(pub: false);
        await using (var db = f.Db())
        {
            (await db.Accounts.FindAsync(member))!.Suspended = suspended;
            var profile = (await db.Profiles.FindAsync(member))!;
            profile.Approved = approved;
            profile.Bio = "A private member bio for admin review.";
            profile.Instagram = "https://instagram.com/example";
            profile.ReviewNote = approved ? null : "Please complete your bio.";
            await db.SaveChangesAsync();
        }
        var details = Assert.IsType<ProfileReview>(await f.Call(admin, $"admin/members/{member}"));
        Assert.Equal(member, details.Id);
        Assert.Equal("A private member bio for admin review.", details.Bio);
        Assert.Equal("https://instagram.com/example", details.Instagram);
        Assert.Equal(approved ? null : "Please complete your bio.", details.ReviewNote);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => f.Call(viewer, $"admin/members/{member}"))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, $"admin/members/{member}"))).Status);
        if (!approved || suspended)
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(viewer, $"people/{member}"))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, $"people/{member}"))).Status);
    }
}
