using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class ConnectionIntroductionTests(DatabaseFixture f)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acceptance_preserves_introduction_once_without_push_and_retains_history_on_reconnection(bool reciprocal)
    {
        var sender = await f.Member(); var recipient = await f.Member();
        var key = CommunityService.Pair(sender, recipient);
        await f.Call(sender, "connections", new ConnectionInput(recipient, "Hello from the request"), "POST");
        await f.Call(recipient, reciprocal ? "connections" : "connections/accept", new ConnectionInput(sender), "POST");
        await f.Call(sender, "connections", new ConnectionInput(recipient), "POST");
        await using (var db = f.Db())
        {
            var message = Assert.Single(await db.Messages.Where(m => m.Conversation == key).ToListAsync());
            Assert.Equal(sender, message.SenderId);
            Assert.Equal("Hello from the request", message.Text);
            Assert.False(message.PushPending);
        }
        var history = (MessagePage)await f.Call(recipient, $"conversations/{key}/messages");
        Assert.Equal("Hello from the request", Assert.Single(history.Items).Text);
        await f.Call(sender, "connections/remove", new ConnectionInput(recipient), "POST");
        await f.Call(recipient, "connections", new ConnectionInput(sender, "A new request"), "POST");
        await f.Call(sender, "connections/accept", new ConnectionInput(recipient), "POST");
        await using var finalDb = f.Db();
        var messages = await finalDb.Messages.Where(m => m.Conversation == key).OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(new[] { "Hello from the request", "A new request" }, messages.Select(m => m.Text));
        Assert.Equal(recipient, messages[1].SenderId);
    }
}
