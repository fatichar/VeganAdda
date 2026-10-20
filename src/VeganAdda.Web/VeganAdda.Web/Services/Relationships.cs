using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public partial class CommunityService
{
    private async Task<object> Connections()
    {
        var me = await User(true); var result = new List<ConnectionItem>();
        var rows = await db.Relationships.Where(r => (r.A == me.Id || r.B == me.Id) && (r.State == "pending" || r.State == "accepted")).OrderByDescending(r => r.Updated).ToListAsync();
        foreach (var r in rows)
        {
            var other = r.A == me.Id ? r.B : r.A;
            var p = await VisiblePeople(includeRelatedProfiles: true).SingleOrDefaultAsync(p => p.Id == other);
            if (p != null) result.Add(new(r.Key, await PersonDto(p), r.State, r.RequestedBy != me.Id, r.Introduction, r.Updated));
        }
        return result;
    }
    private async Task<object> ConnectionAction(string action, JsonElement? body)
    {
        var me = await User(true); var input = Read<ConnectionInput>(body);
        if (await db.Profiles.AnyAsync(p => p.Id == me.Id && p.IsVegan == null)) throw new ApiException(409, "Choose your vegan status before connecting with members.");
        if (input.PersonId == me.Id) throw new ApiException(400, "You cannot connect with yourself.");
        var meIsTest = await db.Profiles.AnyAsync(p => p.Id == me.Id && p.TestProfile);
        var targetIsTest = await db.Profiles.AnyAsync(p => p.Id == input.PersonId && p.TestProfile);
        var targetIsAdmin = await db.Accounts.AnyAsync(a => a.Id == input.PersonId && a.Admin);
        if (meIsTest && !targetIsTest && !targetIsAdmin) throw Denied();
        var key = Pair(me.Id, input.PersonId);
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(key);
        var r = await db.Relationships.FindAsync(key);
        if (!await VisiblePeople(includeRelatedProfiles: r?.State is "pending" or "accepted").AnyAsync(p => p.Id == input.PersonId)) throw Missing();
        if (action == "request")
        {
            var intro = Text(input.Introduction, 300);
            if (r?.State == "accepted" || (r?.State == "pending" && r.RequestedBy == me.Id)) return new Ok();
            if (r?.State == "pending")
            {
                // A reciprocal request is an explicit mutual connection.
                r.State = "accepted";
                Notice(input.PersonId, "connection", $"{me.Name} accepted your connection request.", "/messages/" + key);
            }
            else
            {
                r ??= new Relationship { Key = key, A = string.CompareOrdinal(me.Id.ToString(), input.PersonId.ToString()) < 0 ? me.Id : input.PersonId, B = string.CompareOrdinal(me.Id.ToString(), input.PersonId.ToString()) < 0 ? input.PersonId : me.Id };
                if (db.Entry(r).State == EntityState.Detached) db.Relationships.Add(r);
                r.State = "pending"; r.RequestedBy = me.Id; r.Introduction = intro;
                Notice(input.PersonId, "request", $"{me.Name} sent you a connection request.", "/messages/" + key);
            }
        }
        else
        {
            if (r == null) throw Missing();
            var incoming = r.RequestedBy != me.Id;
            if (action == "accept" && r.State == "pending" && incoming) { r.State = "accepted"; Notice(input.PersonId, "connection", $"{me.Name} accepted your connection request.", "/messages/" + key); }
            else if (action == "decline" && r.State == "pending" && incoming) { r.State = "declined"; Notice(input.PersonId, "declined", $"{me.Name} declined your connection request.", "/messages"); }
            else if (action == "cancel" && r.State == "pending" && !incoming) r.State = "cancelled";
            else if (action == "remove" && r.State == "accepted") r.State = "removed";
            else throw new ApiException(409, "This connection has changed. Refresh to see its current state.");
        }
        r!.Updated = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync(); await changes.Changed(me.Id, input.PersonId); return new Ok();
    }
    private async Task<object> PersonAction(Guid id, string action, JsonElement? body)
    {
        var me = await User(true);
        if (id == me.Id) throw new ApiException(400, "Choose another member.");
        var meIsTest = await db.Profiles.AnyAsync(p => p.Id == me.Id && p.TestProfile);
        var targetIsTest = await db.Profiles.AnyAsync(p => p.Id == id && p.TestProfile);
        var targetIsAdmin = await db.Accounts.AnyAsync(a => a.Id == id && a.Admin);
        if (meIsTest && action != "unblock" && !targetIsTest && !targetIsAdmin) throw Denied();
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(Pair(me.Id, id));
        if (action == "unblock")
        {
            await db.Blocks.Where(b => b.Owner == me.Id && b.Subject == id).ExecuteDeleteAsync();
            await tx.CommitAsync(); await changes.Changed(me.Id, id); return new Ok();
        }
        if (!await VisiblePeople(includeRelatedProfiles: true).AnyAsync(p => p.Id == id)) throw Missing();
        switch (action)
        {
            case "save":
                var existing = await db.Saved.FindAsync(me.Id, id, "person");
                if (existing == null) db.Saved.Add(new() { UserId = me.Id, TargetId = id, Kind = "person" }); else db.Saved.Remove(existing);
                break;
            case "block":
                if (!await db.Blocks.AnyAsync(b => b.Owner == me.Id && b.Subject == id)) db.Blocks.Add(new() { Owner = me.Id, Subject = id });
                // Blocking pauses messaging through CanMessage without removing mutual consent.
                await db.Saved.Where(s => s.Kind == "person" && ((s.UserId == me.Id && s.TargetId == id) || (s.UserId == id && s.TargetId == me.Id))).ExecuteDeleteAsync();
                break;
            case "report":
                var report = Read<ReportInput>(body);
                db.Reports.Add(new() { ReporterId = me.Id, SubjectId = id, Reason = Text(report.Reason, 1000, true) });
                break;
            default: throw Missing();
        }
        await db.SaveChangesAsync(); await tx.CommitAsync(); await changes.Changed(me.Id, id); return new Ok();
    }
    private async Task<Relationship> ConversationAccess(string key)
    {
        var me = await User(true);
        return await db.Relationships.SingleOrDefaultAsync(r => r.Key == key && (r.A == me.Id || r.B == me.Id)) ?? throw Missing();
    }
    private async Task<bool> CanMessage(Relationship r)
    {
        if (r.State != "accepted" || await Blocked(r.A, r.B) || await db.Accounts.CountAsync(a => (a.Id == r.A || a.Id == r.B) && !a.Suspended) != 2) return false;
        var testA = await db.Profiles.AnyAsync(p => p.Id == r.A && p.TestProfile);
        var testB = await db.Profiles.AnyAsync(p => p.Id == r.B && p.TestProfile);
        var adminA = await db.Accounts.AnyAsync(a => a.Id == r.A && a.Admin);
        var adminB = await db.Accounts.AnyAsync(a => a.Id == r.B && a.Admin);
        return !(testA && !testB && !adminB) && !(testB && !testA && !adminA);
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
    private async Task<object> Conversations()
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
            var canReconnect = r.State == "removed" && !unavailable && await VisiblePeople().AnyAsync(p => p.Id == other);
            results.Add(new(r.Key, p?.Name ?? "Unavailable member", p?.Photo ?? "", latest?.Text ?? "Say hello", await db.Messages.CountAsync(m => m.Conversation == r.Key && m.SenderId != me.Id && m.Id > (r.A == me.Id ? r.ReadA : r.ReadB)), latest?.SentAt ?? r.Updated, await CanMessage(r), unavailable && !blockedByMe ? null : other, blockedByMe, canReconnect));
        }
        return results.OrderByDescending(c => c.Updated).ToList();
    }
    private async Task<object> Messages(string key, long before, long? after)
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
    private async Task<object> SendMessage(string key, MessageInput input)
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
    private async Task<object> MarkRead(string key, ReadInput input)
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
