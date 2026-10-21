using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;
using VeganAdda.Web.Features.Verification;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class AdminMemberTests(DatabaseFixture f)
{
    [Fact]
    public async Task Waiting_verification_filter_requires_a_submitted_reviewable_request()
    {
        var admin = await f.Member(admin: true);
        var pending = await f.Member();
        var verified = await f.Member();
        var notRequested = await f.Member();
        var nonVegan = await f.Member();
        var suspended = await f.Member();
        var incomplete = await f.Member();
        await using (var db = f.Db())
        {
            foreach (var id in new[] { pending, notRequested, nonVegan, suspended, incomplete })
                (await db.Profiles.FindAsync(id))!.VeganVerified = false;
            (await db.Profiles.FindAsync(nonVegan))!.IsVegan = false;
            (await db.Accounts.FindAsync(suspended))!.Suspended = true;
            (await db.Profiles.FindAsync(incomplete))!.City = "";
            await db.SaveChangesAsync();
        }
        // Only a submitted request enters the review queue. Status alone is insufficient.
        await f.Feature<VerificationService, Ok>(pending, service => service.SubmitVerification(
            new VerificationSubmission("I have been vegan for several years.", [], null, Guid.NewGuid())));
        await using (var db = f.Db())
        {
            // Historical requests must still be excluded once a member becomes ineligible.
            foreach (var id in new[] { verified, nonVegan, suspended, incomplete })
                db.VerificationRequests.Add(new() { ApplicantId = id, ClientId = Guid.NewGuid(), Explanation = "Earlier request" });
            await db.SaveChangesAsync();
        }
        var rows = (await AllMembers(admin, "pending")).People;
        Assert.Contains(rows, p => p.Id == pending);
        foreach (var id in new[] { verified, notRequested, nonVegan, suspended, incomplete })
            Assert.DoesNotContain(rows, p => p.Id == id);
        Assert.All(rows, p => Assert.True(p.HasProfile && p.IsVegan == true && !p.VeganVerified && !p.Suspended));
    }

    [Fact]
    public async Task Approval_filters_separate_approved_pending_and_incomplete_members()
    {
        var admin = await f.Member(admin: true);
        var approved = await f.Member();
        var pending = await f.Member();
        var incomplete = new Account { Name = "Incomplete member", Subject = Guid.NewGuid().ToString() };
        await using (var db = f.Db())
        {
            (await db.Profiles.FindAsync(pending))!.VeganVerified = false;
            db.Accounts.Add(incomplete);
            await db.SaveChangesAsync();
        }

        var approvedRows = (await AllMembers(admin, "verified")).People;
        Assert.Contains(approvedRows, p => p.Id == approved);
        Assert.All(approvedRows, p => Assert.True(p.VeganVerified && p.HasProfile));
        var pendingRows = (await AllMembers(admin, "unverified")).People;
        Assert.Contains(pendingRows, p => p.Id == pending);
        Assert.All(pendingRows, p => Assert.True(!p.VeganVerified && p.HasProfile));
        var incompleteRows = (await AllMembers(admin, "no-profile")).People;
        Assert.Contains(incompleteRows, p => p.Id == incomplete.Id);
        Assert.All(incompleteRows, p => Assert.False(p.HasProfile));
        var allRows = (await AllMembers(admin, "all")).People;
        Assert.Contains(allRows, p => p.Id == approved);
        Assert.Contains(allRows, p => p.Id == pending);
        Assert.Contains(allRows, p => p.Id == incomplete.Id);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(admin, "admin?verification=invalid"))).Status);
    }

    [Fact]
    public async Task Admin_members_are_paged_after_the_approval_filter()
    {
        var admin = await f.Member(admin: true);
        var ids = new List<Guid>();
        await using (var db = f.Db())
        {
            for (var i = 0; i < 22; i++)
            {
                var account = new Account { Name = $"!Pagination pending {i:00}", Subject = Guid.NewGuid().ToString() };
                ids.Add(account.Id);
                db.Accounts.Add(account);
                db.Profiles.Add(new() { Id = account.Id, Name = account.Name, VeganVerified = false, AdultConfirmedAt = DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        try
        {
            var first = Assert.IsType<AdminData>(await f.Call(admin, "admin?verification=unverified&section=Members&page=1"));
            var second = Assert.IsType<AdminData>(await f.Call(admin, "admin?verification=unverified&section=Members&page=2"));
            Assert.True(first.MembersTotal >= 22);
            Assert.Equal(20, first.People.Count);
            Assert.Equal(Math.Min(20, first.MembersTotal - 20), second.People.Count);
            Assert.Equal(2, second.PageNumber);
            Assert.Empty(first.People.Select(p => p.Id).Intersect(second.People.Select(p => p.Id)));
            Assert.All(first.People.Concat(second.People), p => Assert.False(p.VeganVerified));
            Assert.All(ids, id => Assert.Contains(id, first.People.Concat(second.People).Select(p => p.Id)));
        }
        finally
        {
            await using var db = f.Db();
            await db.Accounts.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync();
        }
    }

    private async Task<AdminData> AllMembers(Guid admin, string approval)
    {
        var first = Assert.IsType<AdminData>(await f.Call(admin, $"admin?verification={approval}&section=Members&page=1"));
        var people = first.People.ToList();
        var pages = (first.MembersTotal + first.PageSize - 1) / first.PageSize;
        for (var page = 2; page <= pages; page++)
        {
            var next = Assert.IsType<AdminData>(await f.Call(admin, $"admin?verification={approval}&section=Members&page={page}"));
            people.AddRange(next.People);
        }
        return first with { People = people };
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
            (await db.Profiles.FindAsync(pending))!.VeganVerified = false;
            for (var i = 0; i < 201; i++)
            {
                var account = new Account { Name = $"AAA approved {i:000}", Subject = Guid.NewGuid().ToString() };
                fixtureIds.Add(account.Id);
                db.Accounts.Add(account);
                db.Profiles.Add(new() { Id = account.Id, Name = account.Name, VeganVerified = true, AdultConfirmedAt = DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        try
        {
            await f.Feature<VerificationService, Ok>(pending, service => service.SubmitVerification(
                new VerificationSubmission("Please review my vegan status.", [], null, Guid.NewGuid())));
            var data = await AllMembers(admin, "pending");
            Assert.Contains(data.People, p => p.Id == pending);
            Assert.DoesNotContain(data.People, p => fixtureIds.Contains(p.Id));
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
            profile.VeganVerified = approved;
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
        if (suspended)
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(viewer, $"people/{member}"))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, $"people/{member}"))).Status);
    }
}
