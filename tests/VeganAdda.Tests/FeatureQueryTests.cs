using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class FeatureQueryTests(DatabaseFixture f)
{
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Discovery_query_count_is_independent_of_the_number_of_cards()
    {
        var viewer = await f.Member(dating: false);
        var marker = Guid.NewGuid().ToString("N");
        await using (var db = f.Db())
        {
            for (var i = 0; i < 13; i++)
            {
                var account = new Account { Subject = Guid.NewGuid().ToString(), Name = "Query peer" };
                db.Accounts.Add(account);
                db.Profiles.Add(new Profile
                {
                    Id = account.Id,
                    Name = "Query peer",
                    Bio = marker,
                    VeganVerified = true,
                    City = "Pune", State = "Maharashtra", BirthDate = new DateOnly(1990, 1, 1), AdultConfirmedAt = DateTimeOffset.UtcNow,
                    Public = true,
                    IsVegan = true,
                    LookingFor = ["Friendship", "Dating"]
                });
            }
            await db.SaveChangesAsync();
        }
        var full = new QueryCounter();
        var first = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { Search = marker }, "POST", full));
        var last = new QueryCounter();
        var second = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { Search = marker, Page = 2 }, "POST", last));
        Assert.Equal(12, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(full.Count, last.Count);
        Assert.InRange(full.Count, 1, 9);
        Assert.All(first.Items, p => Assert.DoesNotContain("Dating", p.LookingFor));
    }

    [Fact]
    public async Task Inbox_pages_requests_and_chats_and_can_open_a_conversation_outside_the_page()
    {
        var viewer = await f.Member();
        var outsider = await f.Member();
        var chats = new List<string>();
        await using (var db = f.Db())
        {
            for (var i = 0; i < 25; i++)
            {
                var peer = new Account { Subject = Guid.NewGuid().ToString(), Name = "Inbox peer" };
                db.Accounts.Add(peer);
                db.Profiles.Add(new Profile { Id = peer.Id, Name = peer.Name, VeganVerified = true,
                    City = "Pune", State = "Maharashtra", BirthDate = new DateOnly(1990, 1, 1), AdultConfirmedAt = DateTimeOffset.UtcNow, IsVegan = true });
                var key = CommunityService.Pair(viewer, peer.Id);
                var first = string.CompareOrdinal(viewer.ToString(), peer.Id.ToString()) < 0;
                db.Relationships.Add(new Relationship
                {
                    Key = key,
                    A = first ? viewer : peer.Id,
                    B = first ? peer.Id : viewer,
                    State = i < 13 ? "accepted" : "pending",
                    Updated = DateTimeOffset.UtcNow.AddMinutes(-i),
                    RequestedBy = peer.Id
                });
                if (i < 13)
                {
                    chats.Add(key);
                    db.Messages.Add(new ChatMessage { Conversation = key, SenderId = peer.Id, ClientId = Guid.NewGuid(), Text = "History", SentAt = DateTimeOffset.UtcNow.AddMinutes(-i) });
                }
            }
            await db.SaveChangesAsync();
        }
        var inbox = Assert.IsType<Inbox>(await f.Call(viewer, "inbox"));
        Assert.Equal(13, inbox.Chats.Total); Assert.Equal(12, inbox.Requests.Total);
        Assert.Equal(10, inbox.Chats.Items.Count); Assert.Equal(10, inbox.Requests.Items.Count);
        var selected = chats.Single(key => key == chats.Last());
        Assert.DoesNotContain(inbox.Chats.Items, c => c.Key == selected);
        var next = Assert.IsType<Inbox>(await f.Call(viewer, $"inbox?chatPage=2&requestPage=2&key={selected}"));
        Assert.Equal(3, next.Chats.Items.Count); Assert.Equal(2, next.Requests.Items.Count);
        Assert.Equal(selected, next.Selected!.Key);
        Assert.Equal(1, next.Selected.Unread);
        Assert.Empty(inbox.Chats.Items.Select(c => c.Key).Intersect(next.Chats.Items.Select(c => c.Key)));
        Assert.Null(Assert.IsType<Inbox>(await f.Call(outsider, $"inbox?key={selected}")).Selected);
    }

    [Fact]
    public async Task Saved_people_are_paged_after_visibility_filters()
    {
        var viewer = await f.Member();
        await using (var db = f.Db())
        {
            for (var i = 0; i < 15; i++)
            {
                var peer = new Account { Subject = Guid.NewGuid().ToString(), Name = "Saved peer" };
                db.Accounts.Add(peer);
                db.Profiles.Add(new Profile { Id = peer.Id, Name = peer.Name, VeganVerified = i != 14, IsVegan = true, City = "Pune", State = "Maharashtra", BirthDate = new DateOnly(1990, 1, 1), AdultConfirmedAt = DateTimeOffset.UtcNow });
                db.Saved.Add(new Saved { UserId = viewer, TargetId = peer.Id, Kind = "person" });
                if (i == 13) db.Blocks.Add(new Block { Owner = peer.Id, Subject = viewer });
            }
            await db.SaveChangesAsync();
        }
        var first = Assert.IsType<SavedItems>(await f.Call(viewer, "saved"));
        var second = Assert.IsType<SavedItems>(await f.Call(viewer, "saved?peoplePage=2"));
        Assert.Equal(14, first.PeopleTotal); Assert.Equal(12, first.People.Count);
        Assert.Equal(2, second.People.Count);
        Assert.Empty(first.People.Select(p => p.Id).Intersect(second.People.Select(p => p.Id)));
    }

    [Fact]
    public async Task Removing_a_referrers_verification_does_not_revoke_an_independent_confirmation()
    {
        var admin = await f.Member(admin: true);
        var a = await f.Member(); var b = await f.Member();
        await using (var db = f.Db())
        {
            db.Referrals.Add(new Referral { ReferredId = a, ReferrerId = b });
            await db.SaveChangesAsync();
        }
        await f.Call(admin, "admin/moderate", new ModerateInput(b, "remove-vegan-verification"), "POST");
        Assert.True(Assert.IsType<ProfileInput>(await f.Call(a, "profile")).CanRefer);
        Assert.False(Assert.IsType<ProfileInput>(await f.Call(b, "profile")).CanRefer);
    }
}
