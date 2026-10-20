using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Referrals;
using VeganAdda.Web.Features.Events;
using static VeganAdda.Web.Features.Profiles.BirthDetails;

namespace VeganAdda.Web.Features.Messaging;

public sealed class MessagingService(FeatureContext context, ProfileVisibility visibility) : FeatureService(context)
{
    public async Task<Relationship> ConversationAccess(string key)
    {
        var me = await User(true);
        return await db.Relationships.SingleOrDefaultAsync(r => r.Key == key && (r.A == me.Id || r.B == me.Id)) ?? throw Missing();
    }
    public async Task<bool> CanMessage(Relationship r)
    {
        if (r.State != "accepted" || await Blocked(r.A, r.B) || await db.Accounts.CountAsync(a => (a.Id == r.A || a.Id == r.B) && !a.Suspended) != 2) return false;
        var testA = await db.Profiles.AnyAsync(p => p.Id == r.A && p.TestProfile);
        var testB = await db.Profiles.AnyAsync(p => p.Id == r.B && p.TestProfile);
        var adminA = await db.Accounts.AnyAsync(a => a.Id == r.A && a.Admin);
        var adminB = await db.Accounts.AnyAsync(a => a.Id == r.B && a.Admin);
        return !(testA && !testB && !adminB) && !(testB && !testA && !adminA);
    }
    public async Task<bool> HiddenTestConversation(Relationship r, Guid viewer)
    {
        if (await db.Accounts.AnyAsync(a => a.Id == viewer && a.Admin)) return false;
        var testA = await db.Profiles.AnyAsync(p => p.Id == r.A && p.TestProfile);
        var testB = await db.Profiles.AnyAsync(p => p.Id == r.B && p.TestProfile);
        if (!testA && !testB) return false;
        var other = r.A == viewer ? r.B : r.A;
        return !await db.Accounts.AnyAsync(a => a.Id == other && a.Admin);
    }
    public async Task<List<Conversation>> Conversations()
    {
        var me = await User(true); var results = new List<Conversation>();
        foreach (var r in await db.Relationships.Where(r => r.A == me.Id || r.B == me.Id).ToListAsync())
        {
            if (await HiddenTestConversation(r, me.Id)) continue;
            var latest = await db.Messages.Where(m => m.Conversation == r.Key).OrderByDescending(m => m.Id).FirstOrDefaultAsync();
            var other = r.A == me.Id ? r.B : r.A;
            var blockedByMe = await db.Blocks.AnyAsync(b => b.Owner == me.Id && b.Subject == other);
            if (latest == null && r.State != "accepted" && !blockedByMe) continue;
            var unavailable = await Blocked(me.Id, other) || !await db.Accounts.AnyAsync(a => a.Id == other && !a.Suspended);
            var isAdmin = await db.Accounts.AnyAsync(a => a.Id == me.Id && a.Admin);
            var p = unavailable ? null : await db.Profiles.FindAsync(other);
            if (p?.TestProfile == true && other != me.Id && !isAdmin) p = null;
            var canReconnect = r.State == "removed" && !unavailable && await visibility.Query().AnyAsync(p => p.Id == other);
            results.Add(new(r.Key, p?.Name ?? "Unavailable member", p?.Photo ?? "", latest?.Text ?? "Say hello", await db.Messages.CountAsync(m => m.Conversation == r.Key && m.SenderId != me.Id && m.Id > (r.A == me.Id ? r.ReadA : r.ReadB)), latest?.SentAt ?? r.Updated, await CanMessage(r), unavailable && !blockedByMe ? null : other, blockedByMe, canReconnect));
        }
        return results.OrderByDescending(c => c.Updated).ToList();
    }
    public async Task<MessagePage> Messages(string key, long before, long? after)
    {
        var r = await ConversationAccess(key);
        if (await HiddenTestConversation(r, Viewer!.Value)) throw Missing();
        if (after.HasValue)
        {
            var missed = await db.Messages.Where(m => m.Conversation == key && m.Id > after.Value).OrderBy(m => m.Id).Take(41).ToListAsync();
            return new MessagePage(missed.Take(40).Select(m => new Message(m.Id, m.SenderId, m.ClientId, m.Text, m.SentAt)).ToList(), false, await CanMessage(r), missed.Count > 40);
        }
        var messages = await db.Messages.Where(m => m.Conversation == key && m.Id < before).OrderByDescending(m => m.Id).Take(41).ToListAsync();
        return new MessagePage(messages.Take(40).Reverse().Select(m => new Message(m.Id, m.SenderId, m.ClientId, m.Text, m.SentAt)).ToList(), messages.Count > 40, await CanMessage(r));
    }
    public async Task<Message> SendMessage(string key, MessageInput input)
    {
        var me = await User(true); var text = Text(input.Text, 2000, true);
        if (input.ClientId == Guid.Empty) throw new ApiException(400, "A client message ID is required.");
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(key);
        // Lock sender too: a reused UUID across different conversations is consistently rejected.
        await UserLock(me.Id);
        var r = await ConversationAccess(key);
        if (!await CanMessage(r)) throw new ApiException(403, "Messaging is unavailable for this connection. Your existing history is still available.");
        var previous = await db.Messages.SingleOrDefaultAsync(m => m.SenderId == me.Id && m.ClientId == input.ClientId);
        if (previous != null)
        {
            if (previous.Conversation != key || previous.Text != text) throw new ApiException(409, "This message ID has already been used.");
            return new Message(previous.Id, previous.SenderId, previous.ClientId, previous.Text, previous.SentAt);
        }
        var other = r.A == me.Id ? r.B : r.A;
        var message = new ChatMessage { Conversation = key, SenderId = me.Id, ClientId = input.ClientId, Text = text, PushPending = true };
        db.Messages.Add(message); r.Updated = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync(); await changes.Changed(me.Id, other);
        return new Message(message.Id, me.Id, input.ClientId, text, message.SentAt);
    }
    public async Task<Ok> MarkRead(string key, ReadInput input)
    {
        var me = await User(true);
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(key);
        var r = await ConversationAccess(key);
        var max = await db.Messages.Where(m => m.Conversation == key).MaxAsync(m => (long?)m.Id) ?? 0;
        var through = Math.Clamp(input.ThroughId, 0, max);
        var previous = r.A == me.Id ? r.ReadA : r.ReadB;
        if (r.A == me.Id) r.ReadA = Math.Max(r.ReadA, through); else r.ReadB = Math.Max(r.ReadB, through);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        if (through > previous) await changes.Changed(me.Id);
        return new Ok();
    }
}
