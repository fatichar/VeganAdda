using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class ConnectionIntroductionTests(DatabaseFixture f)
{
    [Fact]
    public async Task Damaged_ciphertext_keeps_other_messages_readable_and_prevents_duplicate_retries()
    {
        var sender = await f.Member();
        var recipient = await f.Member();
        var key = CommunityService.Pair(sender, recipient);
        await f.Call(sender, "connections", new ConnectionInput(recipient), "POST");
        await f.Call(recipient, "connections/accept", new ConnectionInput(sender), "POST");
        var path = $"conversations/{key}/messages";
        var first = (Message)await f.Call(sender, path, new MessageInput(Guid.NewGuid(), "Still readable"), "POST");
        var secondInput = new MessageInput(Guid.NewGuid(), "Private body");
        var second = (Message)await f.Call(sender, path, secondInput, "POST");
        var replay = (Message)await f.Call(sender, path, secondInput with { Text = "  Private body  " }, "POST");
        Assert.Equal(second.Id, replay.Id);
        var conflict = await Assert.ThrowsAsync<ApiException>(() => f.Call(sender, path, secondInput with { Text = "Different body" }, "POST"));
        Assert.Equal(409, conflict.Status);
        await using (var db = f.Db())
        {
            var row = await db.Messages.SingleAsync(m => m.Id == second.Id);
            row.EncryptedBody = row.EncryptedBody!.ToArray();
            row.EncryptedBody[^1] ^= 1;
            await db.SaveChangesAsync();
        }

        try
        {
            var history = (MessagePage)await f.Call(recipient, path);
            Assert.Collection(history.Items,
                message => { Assert.Equal(first.Id, message.Id); Assert.Equal(first.Text, message.Text); },
                message => { Assert.Equal(second.Id, message.Id); Assert.Contains("unavailable", message.Text); });
            var missed = (MessagePage)await f.Call(recipient, path + $"?after={first.Id}");
            Assert.Equal(second.Id, Assert.Single(missed.Items).Id);
            var retry = await Assert.ThrowsAsync<ApiException>(() => f.Call(sender, path, secondInput, "POST"));
            Assert.Equal(503, retry.Status);
            await using var finalDb = f.Db();
            Assert.Equal(2, await finalDb.Messages.CountAsync(m => m.Conversation == key));
        }
        finally
        {
            await using var db = f.Db();
            var row = await db.Messages.SingleAsync(m => m.Id == second.Id);
            row.EncryptedBody = row.EncryptedBody!.ToArray();
            row.EncryptedBody[^1] ^= 1;
            await db.SaveChangesAsync();
        }
    }

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
            Assert.Equal(f.ChatEncryption.CurrentKeyId, message.BodyKeyId);
            Assert.False(message.PushPending);
        }
        var history = (MessagePage)await f.Call(recipient, $"conversations/{key}/messages");
        Assert.Equal("Hello from the request", Assert.Single(history.Items).Text);
        await f.Call(sender, "connections/remove", new ConnectionInput(recipient), "POST");
        await f.Call(recipient, "connections", new ConnectionInput(sender, "A new request"), "POST");
        await f.Call(sender, "connections/accept", new ConnectionInput(recipient), "POST");
        await using var finalDb = f.Db();
        var messages = await finalDb.Messages.Where(m => m.Conversation == key).OrderBy(m => m.Id).ToListAsync();
        history = (MessagePage)await f.Call(recipient, $"conversations/{key}/messages");
        Assert.Equal(new[] { "Hello from the request", "A new request" }, history.Items.Select(m => m.Text));
        Assert.Equal(recipient, messages[1].SenderId);
    }
}
