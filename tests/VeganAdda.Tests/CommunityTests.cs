using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

// Real PostgreSQL tests: transaction/advisory locks and unique constraints are part of the behavior.
public sealed class DatabaseFixture : IAsyncLifetime
{
    public string Connection { get; private set; } = "";
    private string name = "veganadda_test_" + Guid.NewGuid().ToString("N");
    public async Task InitializeAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable("TEST_DATABASE") ?? "Host=127.0.0.1;Port=55432;Database=postgres;Username=veganadda";
        await using var c = new NpgsqlConnection(baseConnection); await c.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", c).ExecuteNonQueryAsync();
        var b = new NpgsqlConnectionStringBuilder(baseConnection) { Database = name }; Connection = b.ConnectionString;
        await using var db = Db(); await db.Database.MigrateAsync();
    }
    public CommunityDb Db() => new(new DbContextOptionsBuilder<CommunityDb>().UseNpgsql(Connection).Options);
    public CommunityService Service(CommunityDb db, Guid? id) => new(db, new Actor(id), new Bus(), new ConfigurationBuilder().Build(), new TestEnvironment());
    public async Task<object> Call(Guid? id, string path, object? body = null, string method = "GET")
    { await using var db = Db(); return await Service(db, id).Execute(method, path, body == null ? null : JsonSerializer.SerializeToElement(body, Wire.Json)); }
    public async Task<Guid> Member(bool pub = true, bool dating = false, bool admin = false)
    {
        await using var db = Db(); var u = new Account { Subject = Guid.NewGuid().ToString(), Name = "Test Member", Admin = admin };
        db.Accounts.Add(u); db.Profiles.Add(new() { Id = u.Id, Name = u.Name, City = "Pune", State = "Maharashtra", BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-28), HasExactBirthDate = true, Public = pub, Approved = true, IsVegan = true, AdultConfirmedAt = DateTimeOffset.UtcNow, LookingFor = dating ? ["Friendship", "Dating"] : ["Friendship"], Interests = ["Running"] }); await db.SaveChangesAsync(); return u.Id;
    }
    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools(); var b = new NpgsqlConnectionStringBuilder(Connection) { Database = "postgres" };
        await using var c = new NpgsqlConnection(b.ConnectionString); await c.OpenAsync(); await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", c).ExecuteNonQueryAsync();
    }
    private sealed class Actor(Guid? id) : IActor { public Guid? Id => id; }
    private sealed class Bus : IChangeBus { public Task Changed(params Guid[] users) => Task.CompletedTask; }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "VeganAdda.Web";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<DatabaseFixture>;

[Collection("Postgres")]
public class CommunityTests(DatabaseFixture f)
{
    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    public async Task Referral_visibility_matches_creation_permission(bool verified, bool approved, bool testProfile, bool trustRevoked, bool allowed)
    {
        var id = await f.Member();
        await using (var db = f.Db())
        {
            var account = (await db.Accounts.FindAsync(id))!;
            account.Verified = verified; account.TrustRevoked = trustRevoked;
            var profile = (await db.Profiles.FindAsync(id))!;
            profile.Approved = approved; profile.TestProfile = testProfile;
            await db.SaveChangesAsync();
        }
        Assert.Equal(allowed, Assert.IsType<ProfileInput>(await f.Call(id, "profile")).CanRefer);
        if (allowed)
        {
            var invite = Assert.IsType<ReferralInviteResult>(await f.Call(id, "referrals/invite", null, "POST"));
            Assert.Equal(20, invite.Code.Length);
            Assert.InRange(invite.Expires, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
            await using var db = f.Db();
            Assert.Single(await db.ReferralInvites.Where(i => i.ReferrerId == id).ToListAsync());
        }
        else
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "referrals/invite", null, "POST"))).Status);
            await using var db = f.Db();
            Assert.False(await db.ReferralInvites.AnyAsync(i => i.ReferrerId == id));
        }
    }

    [Fact] public async Task Public_projection_omits_private_profiles_and_dating_even_on_direct_lookup()
    {
        var hidden = await f.Member(false, true); var visible = await f.Member(true, true);
        var page = Assert.IsType<Page<Person>>(await f.Call(null, "people"));
        Assert.DoesNotContain(page.Items, p => p.Id == hidden); Assert.DoesNotContain(page.Items.SelectMany(p => p.LookingFor), i => i == "Dating");
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, "people/" + hidden))).Status);
        var serialized = JsonSerializer.Serialize(await f.Call(null, "people/" + visible), Wire.Json);
        Assert.DoesNotContain("Dating", serialized); Assert.DoesNotContain("Subject", serialized, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("email", serialized, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task Dating_requires_mutual_opt_in_and_private_member_profiles_are_authenticated_only()
    {
        var ordinary = await f.Member(); var dater = await f.Member(dating: true); var subject = await f.Member(false, true);
        Assert.DoesNotContain("Dating", Assert.IsType<Person>(await f.Call(ordinary, "people/" + subject)).LookingFor);
        Assert.Contains("Dating", Assert.IsType<Person>(await f.Call(dater, "people/" + subject)).LookingFor);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => f.Call(ordinary, "people?intent=Dating"))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, "people?event=" + Guid.NewGuid()))).Status);
    }
    [Fact] public async Task Profile_requires_adult_confirmation_and_rejects_unsafe_links()
    {
        var id = await f.Member();
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", new ProfileInput { Name = "Name", AdultConfirmed = false }, "POST"))).Status);
        Assert.Throws<ApiException>(() => CommunityService.Url("javascript:alert(1)"));
        Assert.Throws<ApiException>(() => CommunityService.Url("https://instagram.com.evil.test/a", "instagram.com"));
        Assert.Throws<ApiException>(() => CommunityService.Photo("/media/../../keys/file"));
    }
    [Fact] public async Task Profile_location_is_required_persisted_and_sent_for_review()
    {
        var id = await f.Member();
        var profile = (ProfileInput)await f.Call(id, "profile");
        Assert.Equal("India", profile.Country);
        Assert.Equal("Maharashtra", profile.State);
        profile.State = " ";
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", profile, "POST"))).Status);
        profile.State = "Karnataka"; profile.Country = "";
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(id, "profile", profile, "POST"))).Status);
        profile.Country = "India"; profile.City = "Bengaluru";
        await f.Call(id, "profile", profile, "POST");
        var saved = (ProfileInput)await f.Call(id, "profile");
        Assert.Equal("Karnataka", saved.State); Assert.Equal("Bengaluru", saved.City); Assert.False(saved.Approved);
        await using var db = f.Db();
        var stored = await db.Profiles.FindAsync(id);
        Assert.Equal("India", stored!.Country); Assert.Equal("Karnataka", stored.State);
    }
    [Fact] public async Task Requests_prevent_self_and_duplicate_and_serialize_simultaneous_reciprocals()
    {
        var a = await f.Member(); var b = await f.Member();
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(a, "connections", new ConnectionInput(a), "POST"))).Status);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => f.Call(a, "connections", new ConnectionInput(b, "hello"), "POST")));
        await using (var db = f.Db()) Assert.Equal(1, await db.Relationships.CountAsync(r => r.Key == CommunityService.Pair(a, b)));
        await f.Call(b, "connections", new ConnectionInput(a), "POST");
        await using (var db = f.Db()) Assert.Equal("accepted", (await db.Relationships.FindAsync(CommunityService.Pair(a, b)))!.State);
        var c = await f.Member(); var d = await f.Member();
        await Task.WhenAll(f.Call(c, "connections", new ConnectionInput(d), "POST"), f.Call(d, "connections", new ConnectionInput(c), "POST"));
        await using (var db = f.Db()) Assert.Equal("accepted", (await db.Relationships.FindAsync(CommunityService.Pair(c, d)))!.State);
    }
    [Fact] public async Task Connection_transition_permissions_decline_cancel_accept_and_remove()
    {
        var a = await f.Member(); var b = await f.Member(); var stranger = await f.Member();
        await f.Call(a, "connections", new ConnectionInput(b), "POST");
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(a, "connections/accept", new ConnectionInput(b), "POST"))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(stranger, "connections/accept", new ConnectionInput(a), "POST"))).Status);
        await f.Call(b, "connections/decline", new ConnectionInput(a), "POST");
        await f.Call(a, "connections", new ConnectionInput(b), "POST"); await f.Call(a, "connections/cancel", new ConnectionInput(b), "POST");
        await f.Call(a, "connections", new ConnectionInput(b), "POST"); await f.Call(b, "connections/accept", new ConnectionInput(a), "POST");
        await f.Call(a, "connections/remove", new ConnectionInput(b), "POST");
        Assert.False(Assert.IsType<MessagePage>(await f.Call(b, $"conversations/{CommunityService.Pair(a,b)}/messages")).CanMessage);
    }
    private async Task<(Guid A, Guid B, string Key)> Connected()
    { var a = await f.Member(); var b = await f.Member(); await f.Call(a, "connections", new ConnectionInput(b), "POST"); await f.Call(b, "connections/accept", new ConnectionInput(a), "POST"); return (a,b,CommunityService.Pair(a,b)); }
    [Fact] public async Task Decline_notifies_requester_once_and_requests_and_acceptance_link_to_the_inbox()
    {
        var a = await f.Member(); var b = await f.Member();
        await f.Call(a, "connections", new ConnectionInput(b), "POST");
        var request = Assert.Single(Assert.IsType<NotificationList>(await f.Call(b, "notifications")).Items);
        Assert.Equal("/messages/" + CommunityService.Pair(a, b), request.Url);
        await f.Call(b, "connections/decline", new ConnectionInput(a), "POST");
        var decline = Assert.Single(Assert.IsType<NotificationList>(await f.Call(a, "notifications")).Items);
        Assert.Equal("declined", decline.Kind); Assert.Contains("declined", decline.Text); Assert.False(decline.Read);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(b, "connections/decline", new ConnectionInput(a), "POST"))).Status);
        Assert.Single(Assert.IsType<NotificationList>(await f.Call(a, "notifications")).Items);
        await f.Call(a, "connections", new ConnectionInput(b), "POST");
        await f.Call(b, "connections/accept", new ConnectionInput(a), "POST");
        var acceptance = Assert.Single(Assert.IsType<NotificationList>(await f.Call(a, "notifications")).Items, n => n.Kind == "connection");
        Assert.Equal("/messages/" + CommunityService.Pair(a, b), acceptance.Url);
        var chat = Assert.Single(Assert.IsType<List<Conversation>>(await f.Call(a, "conversations")));
        Assert.Equal("Say hello", chat.Latest); Assert.Equal(b, chat.PersonId); Assert.True(chat.CanMessage);
    }
    [Fact] public async Task Messages_only_update_unread_badges_and_legacy_message_notices_are_hidden()
    {
        var (a, b, key) = await Connected(); var outsider = await f.Member();
        await using (var db = f.Db()) { db.Notifications.Add(new() { UserId = b, Kind = "message", Text = "Legacy message", Url = "/messages/" + key }); await db.SaveChangesAsync(); }
        var before = Assert.IsType<NotificationList>(await f.Call(b, "notifications"));
        Message? last = null;
        for (var i = 0; i < 3; i++) last = Assert.IsType<Message>(await f.Call(a, $"conversations/{key}/messages", new MessageInput(Guid.NewGuid(), "Hello " + i), "POST"));
        var after = Assert.IsType<NotificationList>(await f.Call(b, "notifications"));
        Assert.Equal(before.Unread, after.Unread); Assert.Equal(before.Items, after.Items);
        Assert.Equal(3, Assert.IsType<UnreadCounts>(await f.Call(b, "unread")).Messages);
        Assert.Equal(before.Unread, Assert.IsType<UnreadCounts>(await f.Call(b, "unread")).Notifications);
        Assert.Equal(0, Assert.IsType<UnreadCounts>(await f.Call(a, "unread")).Messages);
        Assert.Equal(0, Assert.IsType<UnreadCounts>(await f.Call(outsider, "unread")).Messages);
        Assert.Equal(3, Assert.Single(Assert.IsType<List<Conversation>>(await f.Call(b, "conversations"))).Unread);
        await f.Call(b, $"conversations/{key}/read", new ReadInput(last!.Id - 1), "POST");
        Assert.Equal(1, Assert.IsType<UnreadCounts>(await f.Call(b, "unread")).Messages);
        await f.Call(b, $"conversations/{key}/read", new ReadInput(last.Id), "POST");
        await f.Call(b, $"conversations/{key}/read", new ReadInput(0), "POST");
        Assert.Equal(0, Assert.IsType<UnreadCounts>(await f.Call(b, "unread")).Messages);
        await using var verify = f.Db(); Assert.Equal(1, await verify.Notifications.CountAsync(n => n.UserId == b && n.Kind == "message"));
    }
    [Fact] public async Task Notification_reads_and_single_bulk_and_all_deletions_are_owner_scoped()
    {
        var a = await f.Member(); var b = await f.Member();
        var own = Enumerable.Range(0, 55).Select(i => new VeganAdda.Web.Data.Notification { UserId = a, Text = "Update " + i, Kind = "request", Url = "/messages?tab=requests", Created = DateTimeOffset.UtcNow.AddMinutes(i) }).ToList();
        var foreign = new VeganAdda.Web.Data.Notification { UserId = b, Text = "Private update", Kind = "request" };
        await using (var db = f.Db()) { db.Notifications.AddRange(own); db.Notifications.Add(foreign); await db.SaveChangesAsync(); }
        var firstPage = Assert.IsType<NotificationList>(await f.Call(a, "notifications"));
        Assert.Equal(20, firstPage.Items.Count);
        Assert.Equal(55, firstPage.Total);
        Assert.Equal(1, firstPage.PageNumber);
        var secondPage = Assert.IsType<NotificationList>(await f.Call(a, "notifications?page=2"));
        Assert.Equal(20, secondPage.Items.Count);
        Assert.Equal(2, secondPage.PageNumber);
        Assert.DoesNotContain(firstPage.Items.Select(n => n.Id), id => secondPage.Items.Any(n => n.Id == id));
        Assert.Equal(55, Assert.IsType<UnreadCounts>(await f.Call(a, "unread")).Notifications);
        await f.Call(a, $"notifications/{own[0].Id}/read", null, "POST");
        await f.Call(a, $"notifications/{own[0].Id}/read", null, "POST");
        Assert.Equal(54, Assert.IsType<UnreadCounts>(await f.Call(a, "unread")).Notifications);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(a, $"notifications/{foreign.Id}/read", null, "POST"))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, "notifications", new NotificationSelection([], true), "DELETE"))).Status);
        await f.Call(a, "notifications", new NotificationSelection([own[0].Id]), "DELETE");
        await f.Call(a, "notifications", new NotificationSelection([own[1].Id, own[2].Id, foreign.Id]), "DELETE");
        await using (var db = f.Db()) { Assert.Equal(52, await db.Notifications.CountAsync(n => n.UserId == a)); Assert.False((await db.Notifications.FindAsync(foreign.Id))!.Read); }
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(a, "notifications", new NotificationSelection([]), "DELETE"))).Status);
        await f.Call(a, "notifications/read", null, "POST");
        Assert.Equal(0, Assert.IsType<UnreadCounts>(await f.Call(a, "unread")).Notifications);
        await f.Call(a, "notifications", new NotificationSelection([], true), "DELETE");
        Assert.Empty(Assert.IsType<NotificationList>(await f.Call(a, "notifications")).Items);
        Assert.Single(Assert.IsType<NotificationList>(await f.Call(b, "notifications")).Items);
    }
    [Fact] public async Task Chat_requires_membership_acceptance_and_persists_before_ack_with_duplicate_retry_prevention()
    {
        var (a,b,key) = await Connected(); var outsider = await f.Member();
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(outsider, $"conversations/{key}/messages"))).Status);
        var input = new MessageInput(Guid.NewGuid(), "test text");
        var sends = await Task.WhenAll(Enumerable.Range(0,4).Select(_ => f.Call(a, $"conversations/{key}/messages", input, "POST")));
        Assert.Single(sends.Cast<Message>().Select(m => m.Id).Distinct());
        await using (var db = f.Db()) Assert.Equal(1, await db.Messages.CountAsync(m => m.Conversation == key));
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(a, $"conversations/{key}/messages", input with { Text = "altered" }, "POST"))).Status);
        var convs = Assert.IsType<List<Conversation>>(await f.Call(b, "conversations")); Assert.Equal(1, convs.Single(c => c.Key == key).Unread);
        await f.Call(b, $"conversations/{key}/read", new ReadInput(long.MaxValue), "POST");
        Assert.Equal(0, Assert.IsType<List<Conversation>>(await f.Call(b,"conversations")).Single(c=>c.Key==key).Unread);
    }
    [Fact] public async Task Blocking_pauses_both_directions_and_unblocking_restores_messaging_and_history()
    {
        var a = await f.Member(); var b = await f.Member(); var key = CommunityService.Pair(a,b);
        await f.Call(a, "connections", new ConnectionInput(b), "POST");
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => f.Call(a,$"conversations/{key}/messages",new MessageInput(Guid.NewGuid(),"no"),"POST"))).Status);
        await f.Call(b,"connections/accept",new ConnectionInput(a),"POST");
        await f.Call(a,$"conversations/{key}/messages",new MessageInput(Guid.NewGuid(),"retained"),"POST");
        await f.Call(a,$"people/{b}/block",null,"POST");
        foreach (var id in new[]{a,b}) Assert.Equal(403,(await Assert.ThrowsAsync<ApiException>(()=>f.Call(id,$"conversations/{key}/messages",new MessageInput(Guid.NewGuid(),"no"),"POST"))).Status);
        Assert.Single(Assert.IsType<MessagePage>(await f.Call(b,$"conversations/{key}/messages")).Items);
        Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>f.Call(b,"people/"+a))).Status);
        var blockedChat = Assert.Single(Assert.IsType<List<Conversation>>(await f.Call(a, "conversations")));
        Assert.True(blockedChat.BlockedByMe); Assert.Equal(b, blockedChat.PersonId); Assert.False(blockedChat.CanMessage);
        var otherChat = Assert.Single(Assert.IsType<List<Conversation>>(await f.Call(b, "conversations")));
        Assert.False(otherChat.BlockedByMe); Assert.Null(otherChat.PersonId);
        await f.Call(a,$"people/{b}/unblock",null,"POST");
        foreach (var id in new[] { a, b })
        {
            Assert.True(Assert.IsType<MessagePage>(await f.Call(id, $"conversations/{key}/messages")).CanMessage);
            await f.Call(id, $"conversations/{key}/messages", new MessageInput(Guid.NewGuid(), "resumed"), "POST");
        }
        Assert.Equal(3, Assert.IsType<MessagePage>(await f.Call(a, $"conversations/{key}/messages")).Items.Count);
        Assert.False(Assert.Single(Assert.IsType<List<Conversation>>(await f.Call(a, "conversations"))).BlockedByMe);
    }
    [Fact] public async Task Blocked_empty_chat_remains_available_and_unblocking_does_not_override_another_block()
    {
        var (a, b, key) = await Connected();
        await f.Call(a, $"people/{b}/block", null, "POST");
        Assert.True(Assert.Single(Assert.IsType<List<Conversation>>(await f.Call(a, "conversations"))).BlockedByMe);
        await using (var db = f.Db()) { db.Blocks.Add(new() { Owner = b, Subject = a }); await db.SaveChangesAsync(); }
        await f.Call(a, $"people/{b}/unblock", null, "POST");
        Assert.False(Assert.IsType<MessagePage>(await f.Call(a, $"conversations/{key}/messages")).CanMessage);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => f.Call(a, $"conversations/{key}/messages", new MessageInput(Guid.NewGuid(), "no"), "POST"))).Status);
        await f.Call(b, $"people/{a}/unblock", null, "POST");
        Assert.True(Assert.IsType<MessagePage>(await f.Call(a, $"conversations/{key}/messages")).CanMessage);
    }
    [Fact] public async Task Unblocking_preserves_pending_and_removed_connections_and_offers_reconnection_for_legacy_chats()
    {
        var a = await f.Member(); var b = await f.Member(); var key = CommunityService.Pair(a, b);
        await f.Call(a, "connections", new ConnectionInput(b), "POST");
        await f.Call(a, $"people/{b}/block", null, "POST");
        await f.Call(a, $"people/{b}/unblock", null, "POST");
        Assert.False(Assert.IsType<MessagePage>(await f.Call(a, $"conversations/{key}/messages")).CanMessage);
        await f.Call(b, "connections/accept", new ConnectionInput(a), "POST");
        await f.Call(a, $"conversations/{key}/messages", new MessageInput(Guid.NewGuid(), "history"), "POST");
        await f.Call(b, "connections/remove", new ConnectionInput(a), "POST");
        await f.Call(a, $"people/{b}/block", null, "POST");
        await f.Call(a, $"people/{b}/unblock", null, "POST");
        Assert.False(Assert.IsType<MessagePage>(await f.Call(a, $"conversations/{key}/messages")).CanMessage);
        Assert.True(Assert.Single(Assert.IsType<List<Conversation>>(await f.Call(a, "conversations"))).CanReconnect);
        await f.Call(a, "connections", new ConnectionInput(b), "POST");
        await f.Call(b, "connections/accept", new ConnectionInput(a), "POST");
        Assert.True(Assert.IsType<MessagePage>(await f.Call(a, $"conversations/{key}/messages")).CanMessage);
    }
    [Fact] public async Task History_has_stable_older_pagination_and_removed_connection_cannot_send()
    {
        var (a,b,key)=await Connected();
        await using(var db=f.Db()) { for(int i=0;i<45;i++) db.Messages.Add(new(){Conversation=key,SenderId=a,ClientId=Guid.NewGuid(),Text="History "+i}); await db.SaveChangesAsync(); }
        var first=Assert.IsType<MessagePage>(await f.Call(b,$"conversations/{key}/messages")); Assert.Equal(40,first.Items.Count); Assert.True(first.HasOlder);
        var older=Assert.IsType<MessagePage>(await f.Call(b,$"conversations/{key}/messages?before={first.Items[0].Id}")); Assert.Equal(5,older.Items.Count); Assert.False(older.HasOlder);
        Assert.Empty(first.Items.Select(m=>m.Id).Intersect(older.Items.Select(m=>m.Id)));
        var missed=Assert.IsType<MessagePage>(await f.Call(b,$"conversations/{key}/messages?after=0"));
        Assert.Equal(40,missed.Items.Count); Assert.True(missed.HasNewer);
        var rest=Assert.IsType<MessagePage>(await f.Call(b,$"conversations/{key}/messages?after={missed.Items[^1].Id}"));
        Assert.Equal(5,rest.Items.Count); Assert.False(rest.HasNewer);
        await f.Call(b,"connections/remove",new ConnectionInput(a),"POST");
        Assert.Equal(403,(await Assert.ThrowsAsync<ApiException>(()=>f.Call(a,$"conversations/{key}/messages",new MessageInput(Guid.NewGuid(),"no"),"POST"))).Status);
    }
    [Fact] public async Task Events_require_independent_attendee_consent_and_saves_are_private()
    {
        var a=await f.Member(); var b=await f.Member(); var admin=await f.Member(admin:true);
        var e=new EventInput{Title="Test gathering",Description="Verified test details",City="Pune",Venue="Test venue",Organizer="Test organizer",Published=true,Start=DateTimeOffset.UtcNow.AddDays(1),End=DateTimeOffset.UtcNow.AddDays(1).AddHours(1)};
        await f.Call(admin,"admin/events",e,"POST");
        Guid id; await using(var db=f.Db()) id=(await db.Events.SingleAsync(x=>x.Title==e.Title)).Id;
        await f.Call(a,$"events/{id}/attendance",new AttendanceInput(true,false),"POST");
        Assert.DoesNotContain(Assert.IsType<Page<Person>>(await f.Call(b,"people?event="+id)).Items,p=>p.Id==a);
        await f.Call(a,$"events/{id}/attendance",new AttendanceInput(true,true),"POST");
        Assert.Contains(Assert.IsType<Page<Person>>(await f.Call(b,"people?event="+id)).Items,p=>p.Id==a);
        await f.Call(a,$"people/{b}/save",null,"POST"); await f.Call(a,$"events/{id}/save",null,"POST");
        Assert.Single(Assert.IsType<SavedItems>(await f.Call(a,"saved")).People);
        Assert.Empty(Assert.IsType<SavedItems>(await f.Call(b,"saved")).People);
        await f.Call(a,$"events/{id}/attendance",new AttendanceInput(false,false),"POST");
        Assert.DoesNotContain(Assert.IsType<Page<Person>>(await f.Call(b,"people?event="+id)).Items,p=>p.Id==a);
    }
    [Fact] public async Task Admin_permissions_feature_consent_and_suspension_are_enforced()
    {
        var admin=await f.Member(admin:true); var user=await f.Member();
        Assert.Equal(403,(await Assert.ThrowsAsync<ApiException>(()=>f.Call(user,"admin"))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<ApiException>(()=>f.Call(admin,"admin/moderate",new ModerateInput(user,"feature"),"POST"))).Status);
        await f.Call(admin,"admin/moderate",new ModerateInput(user,"suspend"),"POST");
        Assert.Equal(403,(await Assert.ThrowsAsync<ApiException>(()=>f.Call(user,"session"))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>f.Call(null,"people/"+user))).Status);
    }
    [Fact] public async Task Account_deletion_removes_conversations_and_anonymizes_reports()
    {
        var (a,b,key)=await Connected();
        await f.Call(a,$"conversations/{key}/messages",new MessageInput(Guid.NewGuid(),"delete history"),"POST");
        await f.Call(b,$"people/{a}/report",new ReportInput(a,"Test report"),"POST");
        await f.Call(a,"account",new DeleteInput("DELETE"),"DELETE");
        await using var db=f.Db(); Assert.False(await db.Profiles.AnyAsync(p=>p.Id==a)); Assert.False(await db.Messages.AnyAsync(m=>m.Conversation==key)); Assert.True(await db.Reports.AnyAsync(r=>r.SubjectId==null&&r.ReporterId==b));
    }
}
