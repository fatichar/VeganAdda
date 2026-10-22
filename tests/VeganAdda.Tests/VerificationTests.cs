using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Verification;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class VerificationTests(DatabaseFixture f)
{
    private Task<T> As<T>(Guid? id, Func<VerificationService, Task<T>> operation) => f.Feature(id, operation);
    private static VerificationSubmission Input(Guid? referrer = null, string explanation = "", string[]? links = null)
        => new(explanation, links ?? [], referrer, Guid.NewGuid());
    private async Task<Guid> Applicant()
    {
        var id = await f.Member();
        await using var db = f.Db();
        (await db.Profiles.FindAsync(id))!.VeganVerified = false;
        await db.SaveChangesAsync(); return id;
    }
    private async Task Connect(Guid a, Guid b)
    {
        var key = FeatureService.Pair(a, b);
        await using var db = f.Db();
        db.Relationships.Add(new() { Key = key, A = a, B = b, State = "accepted", RequestedBy = a });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Verification_requests_raise_existing_chats_and_create_nonmessaging_referral_threads()
    {
        var applicant = await Applicant(); var referrer = await f.Member(); var other = await f.Member(); var unconnected = await f.Member();
        await Connect(applicant, referrer); await Connect(other, referrer);
        var key = FeatureService.Pair(applicant, referrer);
        await f.Call(applicant, $"conversations/{key}/messages", new MessageInput(Guid.NewGuid(), "Earlier chat"), "POST");
        await f.Call(other, $"conversations/{FeatureService.Pair(other, referrer)}/messages", new MessageInput(Guid.NewGuid(), "More recent chat"), "POST");
        Assert.NotEqual(key, ((Inbox)await f.Call(referrer, "inbox")).Chats.Items[0].Key);
        var input = new VerificationSubmission("", [], null, Guid.NewGuid(), [referrer, unconnected]);
        await As(applicant, s => s.SubmitVerification(input));
        Assert.Equal(key, ((Inbox)await f.Call(referrer, "inbox")).Chats.Items[0].Key);
        var newKey = FeatureService.Pair(applicant, unconnected);
        var thread = Assert.Single(((Inbox)await f.Call(unconnected, "inbox")).Chats.Items);
        Assert.Equal(newKey, thread.Key); Assert.True(thread.ReferralOnly); Assert.False(thread.CanMessage);
        Assert.False(((MessagePage)await f.Call(unconnected, $"conversations/{newKey}/messages")).CanMessage);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => f.Call(unconnected, $"conversations/{newKey}/messages", new MessageInput(Guid.NewGuid(), "No consent"), "POST"))).Status);
        Assert.True(Assert.Single(await As(unconnected, s => s.GetReferralCards(applicant))).CanRespond);
        await using var db = f.Db(); Assert.False(await db.Messages.AnyAsync(m => m.Conversation == newKey));
    }

    [Fact]
    public async Task Admin_pending_badge_counts_actionable_queues_and_supports_incomplete_admins()
    {
        var admin = await f.Member(admin: true); var applicant = await Applicant(); var referrer = await f.Member();
        await using (var db = f.Db()) { db.Profiles.Remove((await db.Profiles.FindAsync(admin))!); await db.SaveChangesAsync(); }
        var baseline = ((UnreadCounts)await f.Call(admin, "unread")).PendingAdminActions;
        await As(applicant, s => s.SubmitVerification(Input(referrer)));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        var reportId = Guid.NewGuid(); var eventId = Guid.NewGuid();
        await using (var db = f.Db())
        {
            db.Reports.Add(new() { Id = reportId, ReporterId = applicant, SubjectId = referrer, Reason = "Other" });
            db.Events.Add(new() { Id = eventId, Title = "Pending event", PendingReview = true, Start = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(baseline + 3, ((UnreadCounts)await f.Call(admin, "unread")).PendingAdminActions);
        Assert.Equal(0, ((UnreadCounts)await f.Call(referrer, "unread")).PendingAdminActions);
        await f.Call(admin, "admin/moderate", new ModerateInput(reportId, "resolve"), "POST");
        await f.Call(admin, "admin/profile-review", new VeganVerificationInput(applicant, true, RequestId: request.Id), "POST");
        await f.Feature<VeganAdda.Web.Features.Events.EventsService, Ok>(admin, s => s.ReviewEvent(new(eventId, true)));
        Assert.Equal(baseline, ((UnreadCounts)await f.Call(admin, "unread")).PendingAdminActions);
    }

    [Fact]
    public async Task Multiple_referrers_receive_private_independent_requests_and_responses()
    {
        var applicant = await Applicant(); var first = await f.Member(); var second = await f.Member(); var stranger = await f.Member();
        var input = new VerificationSubmission("Private proof", [], null, Guid.NewGuid(), [first, second, first]);
        await As(applicant, s => s.SubmitVerification(input));
        await As(applicant, s => s.SubmitVerification(input));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        Assert.Equal(2, request.Referrers!.Length);
        foreach (var id in new[] { first, second })
        {
            Assert.True(Assert.Single(await As(id, s => s.GetReferralCards(applicant))).CanRespond);
            await using var db = f.Db();
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == id && n.Kind == "referral-request"));
        }
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => As(stranger, s => s.RespondToReferral(request.Id, new(true))))).Status);
        await As(first, s => s.RespondToReferral(request.Id, new(false)));
        await As(second, s => s.RespondToReferral(request.Id, new(true)));
        await As(second, s => s.RespondToReferral(request.Id, new(true)));
        var updated = (await As(applicant, s => s.GetVerification())).Request!;
        Assert.Equal("Declined", updated.Referrers!.Single(r => r.Id == first).Status);
        Assert.Equal("Confirmed", updated.Referrers!.Single(r => r.Id == second).Status);
        Assert.False(((ProfileInput)await f.Call(applicant, "profile")).VeganVerified);
        var serialized = JsonSerializer.Serialize(await As(first, s => s.GetReferralCards(applicant)), Wire.Json);
        Assert.DoesNotContain("Private proof", serialized); Assert.DoesNotContain(second.ToString(), serialized);
        await As(applicant, s => s.WithdrawVerification(request.Id));
        foreach (var id in new[] { first, second }) Assert.False(Assert.Single(await As(id, s => s.GetReferralCards(applicant))).CanRespond);
    }

    [Fact]
    public async Task Multiple_referrers_validate_atomically_and_deleted_referrers_preserve_other_responses()
    {
        var applicant = await Applicant(); var first = await f.Member(); var second = await f.Member(); var unverified = await Applicant();
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => As(applicant, s => s.SubmitVerification(new("", [], null, Guid.NewGuid(), [first, unverified]))))).Status);
        Assert.Null((await As(applicant, s => s.GetVerification())).Request);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => As(applicant, s => s.SubmitVerification(new("", [], null, Guid.NewGuid(), Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToArray()))))).Status);
        await As(applicant, s => s.SubmitVerification(new("", [], first, Guid.NewGuid(), [second])));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        await As(second, s => s.RespondToReferral(request.Id, new(true)));
        await f.Call(first, "account", new DeleteInput("DELETE"), "DELETE");
        var details = (await As(applicant, s => s.GetVerification())).Request!;
        Assert.Equal("Unavailable", details.Referrers!.Single(r => r.Id == null).Status);
        Assert.Equal("Confirmed", details.Referrers!.Single(r => r.Id == second).Status);
    }

    [Fact]
    public async Task Migration_preserves_legacy_referral_response_and_timestamp()
    {
        var isolated = new DatabaseFixture(); await isolated.InitializeAsync();
        try
        {
            var applicant = await isolated.Member(); var referrer = await isolated.Member();
            await using var db = isolated.Db();
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync("20261022095620_ReportDetailsAndScreenshots");
            var id = Guid.NewGuid(); var client = Guid.NewGuid(); var time = DateTimeOffset.UtcNow.AddDays(-1);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "VerificationRequests" ("Id", "ApplicantId", "ClientId", "Status", "Explanation", "EvidenceUrls", "ReferrerId", "ReferralStatus", "Submitted", "Responded")
                VALUES ({id}, {applicant}, {client}, 'Under review', 'Legacy proof', ARRAY[]::varchar[], {referrer}, 'Confirmed', {time}, {time})
                """);
            await migrator.MigrateAsync();
            var row = await db.VerificationReferrals.SingleAsync(r => r.RequestId == id);
            Assert.Equal(referrer, row.ReferrerId); Assert.Equal("Confirmed", row.Status);
            Assert.True((time - row.Responded!.Value).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.Equal("Legacy proof", (await db.VerificationRequests.SingleAsync(r => r.Id == id)).Explanation);
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task Explicit_requests_notify_reviewers_and_only_selected_referrer_can_confirm()
    {
        var applicant = await Applicant(); var referrer = await f.Member(); var stranger = await f.Member();
        var admin = await f.Member(admin: true); var moderator = await f.Member(); var inactiveModerator = await Applicant();
        await using (var db = f.Db())
        {
            (await db.Accounts.FindAsync(moderator))!.Moderator = true;
            (await db.Accounts.FindAsync(inactiveModerator))!.Moderator = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(VerificationStates.NotRequested, (await As(applicant, s => s.GetVerification())).Status);
        var input = Input(referrer, "Private evidence description", ["https://example.org/vegan-evidence"]);
        await As(applicant, s => s.SubmitVerification(input));
        await As(applicant, s => s.SubmitVerification(input)); // Transport retry is idempotent.
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        foreach (var reviewer in new[] { admin, moderator })
        {
            await using var db = f.Db();
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == reviewer && n.Kind == "verification-request"));
        }
        await using (var db = f.Db())
        {
            Assert.False(await db.Notifications.AnyAsync(n => n.UserId == inactiveModerator && n.Kind == "verification-request"));
            Assert.False(await db.Messages.AnyAsync(m => m.Conversation == FeatureService.Pair(applicant, referrer)));
            Assert.Contains(await db.Notifications.Where(n => n.UserId == referrer).ToListAsync(), n => n.Url == "/messages/" + FeatureService.Pair(applicant, referrer));
        }
        var cards = await As(referrer, s => s.GetReferralCards(applicant));
        Assert.True(Assert.Single(cards).CanRespond);
        var serialized = JsonSerializer.Serialize(cards, Wire.Json);
        Assert.DoesNotContain("Private evidence", serialized); Assert.DoesNotContain("vegan-evidence", serialized);
        Assert.Empty(await As(stranger, s => s.GetReferralCards(applicant)));
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => As(stranger, s => s.RespondToReferral(request.Id, new(true))))).Status);
        await As(referrer, s => s.RespondToReferral(request.Id, new(true)));
        await As(referrer, s => s.RespondToReferral(request.Id, new(true)));
        Assert.Equal("Confirmed", (await As(applicant, s => s.GetVerification())).Request!.ReferralStatus);
        Assert.False(((ProfileInput)await f.Call(applicant, "profile")).VeganVerified);
        var review = (ProfileReview)await f.Call(admin, $"admin/members/{applicant}");
        Assert.Equal(input.Explanation, review.Verification!.Explanation);
        await f.Call(moderator, "moderation/profile-review", new VeganVerificationInput(applicant, true, RequestId: request.Id), "POST");
        Assert.Equal(VerificationStates.Verified, (await As(applicant, s => s.GetVerification())).Status);
        Assert.False(Assert.Single(await As(referrer, s => s.GetReferralCards(applicant))).CanRespond);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(moderator, "moderation/profile-review", new VeganVerificationInput(applicant, false, "More evidence", request.Id), "POST"))).Status);
    }

    [Fact]
    public async Task Search_includes_unconnected_verified_vegans_and_excludes_ineligible_members()
    {
        var applicant = await Applicant(); var eligible = await f.Member(); var unverified = await Applicant();
        var disconnected = await f.Member(); var blocked = await f.Member(); var suspended = await f.Member(); var test = await f.Member();
        foreach (var id in new[] { eligible, unverified, blocked, suspended, test }) await Connect(applicant, id);
        var prefix = "Search" + Guid.NewGuid().ToString("N");
        await using (var db = f.Db())
        {
            foreach (var id in new[] { eligible, unverified, disconnected, blocked, suspended, test })
                (await db.Profiles.FindAsync(id))!.Name = prefix;
            (await db.Accounts.FindAsync(suspended))!.Suspended = true;
            (await db.Profiles.FindAsync(test))!.TestProfile = true;
            db.Blocks.Add(new() { Owner = blocked, Subject = applicant }); await db.SaveChangesAsync();
        }
        Assert.Equal(new[] { eligible, disconnected }.Order(), (await As(applicant, s => s.SearchReferrers(prefix))).Select(p => p.Id).Order());
        Assert.Empty(await As(applicant, s => s.SearchReferrers("S")));
        await As(applicant, s => s.SubmitVerification(Input(disconnected)));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        Assert.True(Assert.Single(await As(disconnected, s => s.GetReferralCards(applicant))).CanRespond);
        await As(disconnected, s => s.RespondToReferral(request.Id, new(true)));
        Assert.False(((ProfileInput)await f.Call(applicant, "profile")).VeganVerified);
        for (var i = 0; i < 12; i++)
        {
            var id = await f.Member(); await Connect(applicant, id);
            await using var db = f.Db(); (await db.Profiles.FindAsync(id))!.Name = prefix + i; await db.SaveChangesAsync();
        }
        Assert.Equal(10, (await As(applicant, s => s.SearchReferrers(prefix))).Count);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("verification")]
    [InlineData("suspend")]
    [InlineData("test")]
    public async Task Referrer_eligibility_is_rechecked_after_submission(string change)
    {
        var applicant = await Applicant(); var referrer = await f.Member(); await Connect(applicant, referrer);
        await As(applicant, s => s.SubmitVerification(Input(referrer)));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        await using (var db = f.Db())
        {
            if (change == "block") db.Blocks.Add(new() { Owner = referrer, Subject = applicant });
            if (change == "disconnect") (await db.Relationships.FindAsync(FeatureService.Pair(applicant, referrer)))!.State = "removed";
            if (change == "verification") (await db.Profiles.FindAsync(referrer))!.VeganVerified = false;
            if (change == "suspend") (await db.Accounts.FindAsync(referrer))!.Suspended = true;
            if (change == "test") (await db.Profiles.FindAsync(referrer))!.TestProfile = true;
            await db.SaveChangesAsync();
        }
        Assert.Contains((await Assert.ThrowsAsync<ApiException>(() => As(referrer, s => s.RespondToReferral(request.Id, new(true))))).Status, new[] { 403, 409 });
        Assert.Equal("Unavailable", (await As(applicant, s => s.GetVerification())).Request!.ReferralStatus);
    }

    [Fact]
    public async Task Removing_connection_does_not_invalidate_referral()
    {
        var applicant = await Applicant(); var referrer = await f.Member(); await Connect(applicant, referrer);
        await As(applicant, s => s.SubmitVerification(Input(referrer)));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        await using (var db = f.Db())
        {
            (await db.Relationships.FindAsync(FeatureService.Pair(applicant, referrer)))!.State = "removed";
            await db.SaveChangesAsync();
        }
        await As(referrer, s => s.RespondToReferral(request.Id, new(true)));
        Assert.Equal("Confirmed", (await As(applicant, s => s.GetVerification())).Request!.ReferralStatus);
    }

    [Fact]
    public async Task Feedback_resubmission_withdrawal_and_nonvegan_changes_close_requests()
    {
        var applicant = await Applicant(); var admin = await f.Member(admin: true);
        await As(applicant, s => s.SubmitVerification(Input(explanation: "My vegan lifestyle")));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => As(applicant, s => s.SubmitVerification(Input(explanation: "Duplicate"))))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(admin, "admin/profile-review", new VeganVerificationInput(applicant, false, " ", request.Id), "POST"))).Status);
        await f.Call(admin, "admin/profile-review", new VeganVerificationInput(applicant, false, "Please explain further", request.Id), "POST");
        Assert.Equal(VerificationStates.ChangesRequested, (await As(applicant, s => s.GetVerification())).Status);
        await As(applicant, s => s.SubmitVerification(Input(links: ["https://example.org/evidence"])));
        var resubmitted = (await As(applicant, s => s.GetVerification())).Request!;
        Assert.NotEqual(request.Id, resubmitted.Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(admin, "admin/profile-review", new VeganVerificationInput(applicant, true, RequestId: request.Id), "POST"))).Status);
        await As(applicant, s => s.WithdrawVerification(resubmitted.Id));
        Assert.Equal(VerificationStates.NotRequested, (await As(applicant, s => s.GetVerification())).Status);
        await As(applicant, s => s.SubmitVerification(Input(explanation: "Resubmit")));
        var profile = (ProfileInput)await f.Call(applicant, "profile"); profile.IsVegan = false;
        await f.Call(applicant, "profile", profile, "POST");
        Assert.Equal(VerificationStates.NotRequested, (await As(applicant, s => s.GetVerification())).Status);
        await using var db = f.Db();
        Assert.False(await db.VerificationRequests.AnyAsync(r => r.ApplicantId == applicant && r.Status == VerificationStates.UnderReview));
    }

    [Fact]
    public async Task Declining_does_not_close_review_and_admin_override_resolves_active_request()
    {
        var applicant = await Applicant(); var referrer = await f.Member(); var admin = await f.Member(admin: true); await Connect(applicant, referrer);
        await As(applicant, s => s.SubmitVerification(Input(referrer)));
        var request = (await As(applicant, s => s.GetVerification())).Request!;
        await As(referrer, s => s.RespondToReferral(request.Id, new(false)));
        Assert.Equal(VerificationStates.UnderReview, (await As(applicant, s => s.GetVerification())).Status);
        Assert.Equal("Declined", (await As(applicant, s => s.GetVerification())).Request!.ReferralStatus);
        await f.Call(admin, "admin/moderate", new ModerateInput(applicant, "confirm-vegan"), "POST");
        Assert.Equal(VerificationStates.Verified, (await As(applicant, s => s.GetVerification())).Request!.Status);
    }

    [Fact]
    public async Task Concurrent_requests_have_one_winner_and_account_deletion_removes_private_evidence()
    {
        var applicant = await Applicant();
        async Task<bool> Submit()
        {
            try { await As(applicant, s => s.SubmitVerification(Input(explanation: "Private evidence"))); return true; }
            catch (ApiException ex) when (ex.Status == 409) { return false; }
        }
        Assert.Equal(1, (await Task.WhenAll(Submit(), Submit())).Count(success => success));
        await f.Call(applicant, "account", new DeleteInput("DELETE"), "DELETE");
        await using var db = f.Db();
        Assert.False(await db.VerificationRequests.AnyAsync(r => r.ApplicantId == applicant));
    }

    [Fact]
    public async Task Evidence_validation_and_request_authorization_are_server_enforced()
    {
        var applicant = await Applicant(); var verified = await f.Member();
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => As(null, s => s.GetVerification()))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => As(applicant, s => s.SubmitVerification(Input())))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => As(applicant, s => s.SubmitVerification(Input(links: ["javascript:alert(1)"]))))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => As(verified, s => s.SubmitVerification(Input(explanation: "Already verified"))))).Status);
        await As(applicant, s => s.SubmitVerification(Input(explanation: "Private")));
        var id = (await As(applicant, s => s.GetVerification())).Request!.Id;
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => As(verified, s => s.WithdrawVerification(id)))).Status);
        var profile = (ProfileInput)await f.Call(applicant, "profile"); profile.ReferralCode = new string('a', 20);
        Assert.Equal(410, (await Assert.ThrowsAsync<ApiException>(() => f.Call(applicant, "profile", profile, "POST"))).Status);
    }

    [Fact]
    public async Task Social_links_are_visible_to_members_but_never_anonymous_visitors()
    {
        var member = await f.Member(); var viewer = await f.Member();
        await using (var db = f.Db())
        {
            var p = (await db.Profiles.FindAsync(member))!;
            p.Instagram = "https://instagram.com/example"; p.LinkedIn = "https://linkedin.com/in/example"; p.Website = "https://example.org";
            await db.SaveChangesAsync();
        }
        var anonymous = (Person)await f.Call(null, $"people/{member}");
        Assert.Null(anonymous.Instagram); Assert.Null(anonymous.LinkedIn); Assert.Null(anonymous.Website);
        var signedIn = (Person)await f.Call(viewer, $"people/{member}");
        Assert.NotNull(signedIn.Instagram); Assert.NotNull(signedIn.LinkedIn); Assert.NotNull(signedIn.Website);
    }
}
