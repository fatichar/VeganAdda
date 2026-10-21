using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Profiles;

namespace VeganAdda.Web.Features.Messaging;

public sealed class MessagingService(FeatureContext context, ProfileVisibility visibility) : FeatureService(context)
{
    private async Task<Relationship> ConversationAccess(string key)
    {
        var me = await User();
        return await db.Relationships.SingleOrDefaultAsync(r => r.Key == key && (r.A == me.Id || r.B == me.Id)) ?? throw Missing();
    }
    private async Task<bool> CanMessage(Relationship relationship)
    {
        var ids = new[] { relationship.A, relationship.B };
        var accounts = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var tests = await db.Profiles.Where(p => ids.Contains(p.Id) && p.TestProfile).Select(p => p.Id).ToHashSetAsync();
        MessagingParty Party(Guid id) => accounts.TryGetValue(id, out var account)
            ? new(!account.Suspended, tests.Contains(id), account.Admin) : new(false, false, false);
        return MessagingPolicy.CanMessage(relationship.State, await Blocked(relationship.A, relationship.B), Party(relationship.A), Party(relationship.B));
    }
    private async Task<bool> HiddenTestConversation(Relationship r, Guid viewer)
    {
        if (await db.Accounts.AnyAsync(a => a.Id == viewer && a.Admin)) return false;
        var testA = await db.Profiles.AnyAsync(p => p.Id == r.A && p.TestProfile);
        var testB = await db.Profiles.AnyAsync(p => p.Id == r.B && p.TestProfile);
        if (!testA && !testB) return false;
        var other = r.A == viewer ? r.B : r.A;
        return !await db.Accounts.AnyAsync(a => a.Id == other && a.Admin);
    }
    public async Task<List<Conversation>> Conversations() => (await ConversationsPage(pageSize: 20, includePending: true)).Items;

    public async Task<Page<Conversation>> ConversationsPage(int page = 1, string? key = null, int pageSize = 10, bool includePending = false)
    {
        var me = await User();
        var myProfile = await db.Profiles.SingleAsync(p => p.Id == me.Id);
        var query = db.Relationships.Where(r => r.A == me.Id || r.B == me.Id);
        if (!includePending) query = query.Where(r => r.State != "pending");
        if (key != null) query = query.Where(r => r.Key == key);
        if (!me.Admin)
            query = query.Where(r => !db.Profiles.Any(p => (p.Id == r.A || p.Id == r.B) && p.TestProfile)
                || db.Accounts.Any(a => a.Id == (r.A == me.Id ? r.B : r.A) && a.Admin));
        query = query.Where(r => r.State == "accepted" || db.Messages.Any(m => m.Conversation == r.Key)
            || db.Blocks.Any(b => b.Owner == me.Id && b.Subject == (r.A == me.Id ? r.B : r.A)));
        var total = await query.CountAsync();
        pageSize = Math.Clamp(pageSize, 1, 20);
        page = Math.Clamp(page, 1, Math.Max(1, (total + pageSize - 1) / pageSize));
        var rows = await query.OrderByDescending(r => db.Messages.Where(m => m.Conversation == r.Key).OrderByDescending(m => m.Id).Select(m => (DateTimeOffset?)m.SentAt).FirstOrDefault() ?? r.Updated)
            .ThenBy(r => r.Key).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var keys = rows.Select(r => r.Key).ToArray();
        if (keys.Length == 0) return new([], total, page, pageSize);
        var ids = rows.Select(r => r.A == me.Id ? r.B : r.A).ToArray();
        var accounts = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var profiles = await db.Profiles.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var blocks = await db.Blocks.Where(b => (b.Owner == me.Id && ids.Contains(b.Subject)) || (b.Subject == me.Id && ids.Contains(b.Owner))).ToListAsync();
        var latest = await db.Messages.Where(m => keys.Contains(m.Conversation)).GroupBy(m => m.Conversation)
            .Select(g => g.OrderByDescending(m => m.Id).First()).ToDictionaryAsync(m => m.Conversation);
        var unread = await (from m in db.Messages join rel in db.Relationships on m.Conversation equals rel.Key
            where keys.Contains(m.Conversation) && m.SenderId != me.Id && m.Id > (rel.A == me.Id ? rel.ReadA : rel.ReadB)
            group m by m.Conversation into g select new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var visible = await visibility.Query().Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToHashSetAsync();
        var result = new List<Conversation>();
        foreach (var rel in rows)
        {
            var other = rel.A == me.Id ? rel.B : rel.A;
            accounts.TryGetValue(other, out var account);
            profiles.TryGetValue(other, out var profile);
            latest.TryGetValue(rel.Key, out var message);
            var blockedByMe = blocks.Any(b => b.Owner == me.Id && b.Subject == other);
            var blocked = blockedByMe || blocks.Any(b => b.Owner == other && b.Subject == me.Id);
            var unavailable = blocked || account == null || account.Suspended;
            var card = unavailable || (profile?.TestProfile == true && !me.Admin) ? null : profile;
            var canMessage = MessagingPolicy.CanMessage(rel.State, blocked,
                new(!me.Suspended, myProfile.TestProfile, me.Admin),
                new(account != null && !account.Suspended, profile?.TestProfile ?? false, account?.Admin ?? false));
            result.Add(new(rel.Key, card?.Name ?? "Unavailable member", card?.Photo ?? "", message?.Text ?? "Say hello",
                unread.GetValueOrDefault(rel.Key), message?.SentAt ?? rel.Updated, canMessage,
                unavailable && !blockedByMe ? null : other, blockedByMe, rel.State == "removed" && !unavailable && visible.Contains(other), card?.IsVegan == true && card.VeganVerified));
        }
        return new(result, total, page, pageSize);
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
        var me = await User(); var text = Text(input.Text, 2000, true);
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
        await db.SaveChangesAsync(); await tx.CommitAsync(); await changes.Changed(ChangeArea.Messaging, me.Id, other);
        return new Message(message.Id, me.Id, input.ClientId, text, message.SentAt);
    }
    public async Task<Ok> MarkRead(string key, ReadInput input)
    {
        var me = await User();
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(key);
        var r = await ConversationAccess(key);
        var max = await db.Messages.Where(m => m.Conversation == key).MaxAsync(m => (long?)m.Id) ?? 0;
        var through = Math.Clamp(input.ThroughId, 0, max);
        var previous = r.A == me.Id ? r.ReadA : r.ReadB;
        if (r.A == me.Id) r.ReadA = Math.Max(r.ReadA, through); else r.ReadB = Math.Max(r.ReadB, through);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        if (through > previous) await changes.Changed(ChangeArea.Messaging, me.Id);
        return new Ok();
    }
}
