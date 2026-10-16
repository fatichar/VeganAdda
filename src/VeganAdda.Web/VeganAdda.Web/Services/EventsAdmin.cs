using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public partial class CommunityService
{
    private async Task<EventCard> EventDto(Gathering e)
    {
        var a = Viewer.HasValue ? await db.Attendance.FindAsync(Viewer.Value, e.Id) : null;
        return new(e.Id, e.Title, e.Cover, e.Description, e.Start, e.End, e.TimeZone, e.City, e.Venue, e.Organizer, e.Registration,
            e.Featured, Viewer.HasValue && await db.Saved.AnyAsync(s => s.UserId == Viewer && s.TargetId == e.Id && s.Kind == "event"), a != null, a?.Visible ?? false, e.Demo);
    }
    private async Task<object> EventQuery(Guid? id, string city, int page)
    {
        var events = db.Events.Where(e => e.Published);
        if (id.HasValue) return await EventDto(await events.SingleOrDefaultAsync(e => e.Id == id) ?? throw Missing());
        events = events.Where(e => e.End >= DateTimeOffset.UtcNow);
        if (city.Length > 0 && city != "All India") events = events.Where(e => e.City == city);
        var total = await events.CountAsync(); var result = new List<EventCard>();
        foreach (var e in await events.OrderByDescending(e => e.Featured).ThenBy(e => e.Start).ThenBy(e => e.Id).Skip((page - 1) * 12).Take(12).ToListAsync()) result.Add(await EventDto(e));
        return new Page<EventCard>(result, total, page);
    }
    private async Task<object> EventAction(Guid id, string action, JsonElement? body)
    {
        var me = await User(true);
        if (!await db.Events.AnyAsync(e => e.Id == id && e.Published)) throw Missing();
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        if (action == "save")
        {
            var s = await db.Saved.FindAsync(me.Id, id, "event");
            if (s != null) db.Saved.Remove(s); else db.Saved.Add(new() { UserId = me.Id, TargetId = id, Kind = "event" });
        }
        else if (action == "attendance")
        {
            var input = Read<AttendanceInput>(body); var a = await db.Attendance.FindAsync(me.Id, id);
            if (!input.Attending && a != null) db.Attendance.Remove(a);
            if (input.Attending)
            {
                if (a == null) { a = new() { UserId = me.Id, EventId = id }; db.Attendance.Add(a); }
                a.Visible = input.Visible;
            }
        }
        else throw Missing();
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new Ok();
    }
    private async Task<object> Admin()
    {
        await User(admin: true);
        var events = await db.Events.OrderBy(e => e.Start).Select(e => new EventInput { Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone, City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = e.Published, Featured = e.Featured }).ToListAsync();
        var reports = await db.Reports.OrderByDescending(r => r.Created).Take(100).Select(r => new ReportView(r.Id, r.ReporterId == null ? "Deleted account" : "Member", r.SubjectId ?? Guid.Empty, r.SubjectId == null ? "Deleted account" : db.Accounts.Where(a => a.Id == r.SubjectId).Select(a => a.Name).FirstOrDefault() ?? "Unavailable", r.Reason, r.Resolved, r.Created)).ToListAsync();
        var people = await db.Accounts.OrderBy(a => a.Name).Take(200).Select(a => new AdminPerson(a.Id, a.Name, a.Suspended,
            db.Profiles.Any(p => p.Id == a.Id && p.Public), db.Profiles.Any(p => p.Id == a.Id && p.FeatureConsent), db.Profiles.Any(p => p.Id == a.Id && p.Featured))).ToListAsync();
        return new AdminData(events, reports, people);
    }
    private async Task<object> SaveEvent(EventInput input)
    {
        await User(admin: true);
        var title = Text(input.Title, 150, true); var description = Text(input.Description, 4000); var city = Text(input.City, 80);
        var venue = Text(input.Venue, 200); var organizer = Text(input.Organizer, 150); var registration = Url(input.Registration);
        var cover = Photo(input.Cover);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); } catch { throw new ApiException(400, "Choose a valid IANA time zone, such as Asia/Kolkata."); }
        if (input.Published && (input.Start == null || input.End == null || input.End <= input.Start || city == "" || venue == "" || organizer == "" || description == "")) throw new ApiException(400, "Published events need verified start/end times, city, venue, organizer and description.");
        var e = input.Id == Guid.Empty ? null : await db.Events.FindAsync(input.Id);
        if (input.Id != Guid.Empty && e == null) throw Missing();
        e ??= new(); if (db.Entry(e).State == EntityState.Detached) db.Events.Add(e);
        e.Title = title; e.Description = description; e.City = city; e.Venue = venue; e.Organizer = organizer; e.Registration = registration;
        e.Cover = cover; e.Start = input.Start?.ToUniversalTime(); e.End = input.End?.ToUniversalTime(); e.TimeZone = input.TimeZone; e.Featured = input.Featured; e.Published = input.Published;
        await db.SaveChangesAsync(); return new Ok();
    }
    private async Task<object> Moderate(ModerateInput input)
    {
        var me = await User(admin: true);
        if (input.Action == "resolve")
        {
            var report = await db.Reports.FindAsync(input.Id) ?? throw Missing(); report.Resolved = true;
        }
        else
        {
            var u = await db.Accounts.FindAsync(input.Id) ?? throw Missing();
            if (u.Admin && input.Action == "suspend") throw new ApiException(400, "Administrator suspension requires an owner database review.");
            if (input.Action is "suspend" or "restore") u.Suspended = input.Action == "suspend";
            else if (input.Action is "feature" or "unfeature")
            {
                var p = await db.Profiles.FindAsync(u.Id) ?? throw Missing();
                if (input.Action == "feature" && (!p.Public || !p.FeatureConsent || u.Suspended)) throw new ApiException(400, "Only consenting, active public profiles can be featured.");
                p.Featured = input.Action == "feature";
            }
            else throw Missing();
        }
        await db.SaveChangesAsync(); await changes.Changed(input.Id); return new Ok();
    }
    private async Task<object> DeleteAccount()
    {
        var me = await User();
        if (me.Admin) throw new ApiException(409, "Transfer administrator access before deleting this account.");
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        var peers = await db.Relationships.Where(r => r.A == me.Id || r.B == me.Id).Select(r => r.A == me.Id ? r.B : r.A).ToArrayAsync();
        await db.Saved.Where(s => s.TargetId == me.Id && s.Kind == "person").ExecuteDeleteAsync();
        // Cascade removes all conversations and both participants' messages in those conversations.
        db.Accounts.Remove(me); await db.SaveChangesAsync(); await tx.CommitAsync();
        var media = config["Storage:MediaPath"] ?? "uploads";
        if (Directory.Exists(media)) foreach (var marker in Directory.EnumerateFiles(media, "*.owner-" + me.Id.ToString("N")))
        { File.Delete(marker[..marker.LastIndexOf(".owner-", StringComparison.Ordinal)]); File.Delete(marker); }
        await changes.Changed(peers); return new Ok();
    }
}
