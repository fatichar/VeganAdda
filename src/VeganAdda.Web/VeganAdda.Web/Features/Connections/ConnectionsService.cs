using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Profiles;

namespace VeganAdda.Web.Features.Connections;

public sealed class ConnectionsService(FeatureContext context, ProfileVisibility visibility, PeopleProjection people) : FeatureService(context)
{
    public async Task<List<ConnectionItem>> Connections() => (await ConnectionsPage(pageSize: 20)).Items;

    public async Task<Page<ConnectionItem>> ConnectionsPage(int page = 1, string state = "", string? key = null, int pageSize = 10)
    {
        var me = await User();
        var relationships = db.Relationships.Where(r => (r.A == me.Id || r.B == me.Id) && (r.State == "pending" || r.State == "accepted"));
        if (state is not ("" or "pending" or "accepted")) throw new ApiException(400, "Choose a supported connection state.");
        if (state != "") relationships = relationships.Where(r => r.State == state);
        if (key != null) relationships = relationships.Where(r => r.Key == key);
        var query = relationships.Join(visibility.Query(true), r => r.A == me.Id ? r.B : r.A, p => p.Id, (r, p) => new { Relationship = r, Profile = p });
        var total = await query.CountAsync();
        pageSize = Math.Clamp(pageSize, 1, 20);
        page = Math.Clamp(page, 1, Math.Max(1, (total + pageSize - 1) / pageSize));
        var rows = await query.OrderByDescending(x => x.Relationship.State == "pending" && x.Relationship.RequestedBy != me.Id)
            .ThenByDescending(x => x.Relationship.Updated).ThenBy(x => x.Relationship.Key)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var cards = (await people.Many(rows.Select(x => x.Profile).ToList())).ToDictionary(p => p.Id);
        return new(rows.Select(x => new ConnectionItem(x.Relationship.Key, cards[x.Profile.Id], x.Relationship.State,
            x.Relationship.RequestedBy != me.Id, x.Relationship.Introduction, x.Relationship.Updated)).ToList(), total, page, pageSize);
    }
    public async Task<Ok> ConnectionAction(string action, JsonElement? body)
    {
        var me = await User(); var input = Read<ConnectionInput>(body);
        if (await db.Profiles.AnyAsync(p => p.Id == me.Id && p.IsVegan == null)) throw new ApiException(409, "Choose your vegan status before connecting with members.");
        if (input.PersonId == me.Id) throw new ApiException(400, "You cannot connect with yourself.");
        var meIsTest = await db.Profiles.AnyAsync(p => p.Id == me.Id && p.TestProfile);
        var targetIsTest = await db.Profiles.AnyAsync(p => p.Id == input.PersonId && p.TestProfile);
        var targetIsAdmin = await db.Accounts.AnyAsync(a => a.Id == input.PersonId && a.Admin);
        if (meIsTest && !targetIsTest && !targetIsAdmin) throw Denied();
        var key = Pair(me.Id, input.PersonId);
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(key);
        var r = await db.Relationships.FindAsync(key);
        if (!await visibility.Query(includeRelatedProfiles: r?.State is "pending" or "accepted").AnyAsync(p => p.Id == input.PersonId)) throw Missing();
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
        await db.SaveChangesAsync(); await tx.CommitAsync(); await changes.Changed(ChangeArea.Connections, me.Id, input.PersonId); return new Ok();
    }
    public async Task<Ok> PersonAction(Guid id, string action, JsonElement? body)
    {
        var me = await User(allowIncomplete: action == "unblock");
        if (id == me.Id) throw new ApiException(400, "Choose another member.");
        var meIsTest = await db.Profiles.AnyAsync(p => p.Id == me.Id && p.TestProfile);
        var targetIsTest = await db.Profiles.AnyAsync(p => p.Id == id && p.TestProfile);
        var targetIsAdmin = await db.Accounts.AnyAsync(a => a.Id == id && a.Admin);
        if (meIsTest && action != "unblock" && !targetIsTest && !targetIsAdmin) throw Denied();
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(Pair(me.Id, id));
        if (action == "unblock")
        {
            await db.Blocks.Where(b => b.Owner == me.Id && b.Subject == id).ExecuteDeleteAsync();
            await tx.CommitAsync(); await changes.Changed(ChangeArea.Connections, me.Id, id); return new Ok();
        }
        if (!await visibility.Query(includeRelatedProfiles: true).AnyAsync(p => p.Id == id)) throw Missing();
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
        await db.SaveChangesAsync(); await tx.CommitAsync(); await changes.Changed(ChangeArea.Connections, me.Id, id); return new Ok();
    }
    public async Task<List<AdminPerson>> GetBlocked()
    {
        var blocker = await User(allowIncomplete: true);
                    return await db.Blocks.Where(b => b.Owner == blocker.Id).Join(db.Profiles, b => b.Subject, p => p.Id, (b, p) => new AdminPerson(p.Id, p.Name, false, false, false)).ToListAsync();
    }
}
