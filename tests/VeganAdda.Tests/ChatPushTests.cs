using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class ChatPushTests(DatabaseFixture f)
{
    sealed class Sender : IChatPushSender
    {
        public bool Fail;
        public List<(string Token, long Message, Guid Recipient)> Sent = [];
        public Task Send(string token, long messageId, Guid recipient, CancellationToken ct)
        {
            if (Fail) throw new IOException("Unavailable");
            Sent.Add((token, messageId, recipient)); return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Queue_targets_recipient_devices_retries_failures_and_does_not_requeue_client_retries()
    {
        var a = await f.Member(); var b = await f.Member(); var outsider = await f.Member();
        await f.Call(a, "connections", new ConnectionInput(b), "POST");
        await f.Call(b, "connections/accept", new ConnectionInput(a), "POST");
        var key = CommunityService.Pair(a, b);
        await using (var db = f.Db())
        {
            db.PushDevices.AddRange(new PushDevice { UserId = a, Token = "sender" }, new PushDevice { UserId = b, Token = "recipient" }, new PushDevice { UserId = outsider, Token = "outsider" });
            await db.SaveChangesAsync();
        }
        using var services = new ServiceCollection().AddDbContext<CommunityDb>(o => o.UseNpgsql(f.Connection)).BuildServiceProvider();
        var sender = new Sender { Fail = true };
        var clock = new ManualClock();
        var worker = new ChatPushWorker(services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), sender, NullLogger<ChatPushWorker>.Instance, clock);
        var input = new MessageInput(Guid.NewGuid(), "Private text must never reach the transport");
        var message = (Message)await f.Call(a, $"conversations/{key}/messages", input, "POST");
        await worker.DeliverPending();
        await using (var db = f.Db()) { var queued = await db.Messages.FindAsync(message.Id); Assert.True(queued!.PushPending); Assert.Equal(1, queued.PushAttempts); }
        clock.Advance(TimeSpan.FromSeconds(6));
        sender.Fail = false;
        await worker.DeliverPending();
        Assert.Equal(("recipient", message.Id, b), Assert.Single(sender.Sent));
        await f.Call(a, $"conversations/{key}/messages", input, "POST");
        await worker.DeliverPending(); Assert.Single(sender.Sent);
        // Read receipts suppress queued deliveries.
        await f.Call(a, $"conversations/{key}/messages", new MessageInput(Guid.NewGuid(), "Already read"), "POST");
        await f.Call(b, $"conversations/{key}/read", new ReadInput(long.MaxValue), "POST");
        await worker.DeliverPending(); Assert.Single(sender.Sent);
        // A block after message commit must suppress pending alerts too.
        await f.Call(a, $"conversations/{key}/messages", new MessageInput(Guid.NewGuid(), "Before block"), "POST");
        await f.Call(b, $"people/{a}/block", null, "POST");
        await worker.DeliverPending(); Assert.Single(sender.Sent);
        await using (var db = f.Db()) Assert.False(await db.Messages.AnyAsync(m => m.Conversation == key && m.PushPending));
    }
}
