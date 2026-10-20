using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public partial class CommunityService(CommunityDb db, IActor actor, IChangeBus changes, IConfiguration config, IWebHostEnvironment env)
{
    private Guid? Viewer => actor.Id;
    private static ApiException Missing() => new(404, "This item is no longer available.");
    private static ApiException Denied() => new(403, "You don't have access to this action.");
    private static T Read<T>(JsonElement? body) where T : class => body?.Deserialize<T>(Wire.Json) ?? throw new ApiException(400, "Please check the form and try again.");
    private async Task<Account> User(bool profile = false, bool admin = false, bool moderator = false)
    {
        var u = await db.Accounts.SingleOrDefaultAsync(x => x.Id == Viewer) ?? throw new ApiException(401, "Sign in to continue.");
        if (u.Suspended) throw new ApiException(403, "Your account is suspended. Contact the community team for help.");
        if (profile && !await db.Profiles.AnyAsync(x => x.Id == u.Id)) throw new ApiException(409, "Create your profile first.");
        if (admin && !u.Admin) throw Denied();
        if (moderator && !u.Admin && (!u.Moderator || !await db.Profiles.AnyAsync(p => p.Id == u.Id && p.IsVegan == true && !p.VeganVerificationPending))) throw Denied();
        return u;
    }
    private async Task ActiveViewer() { if (Viewer.HasValue) await User(); }
    private async Task<bool> Blocked(Guid a, Guid b) => await db.Blocks.AnyAsync(x => (x.Owner == a && x.Subject == b) || (x.Owner == b && x.Subject == a));
    public static string Pair(Guid a, Guid b) => string.CompareOrdinal(a.ToString(), b.ToString()) < 0 ? $"{a:N}_{b:N}" : $"{b:N}_{a:N}";
    private Task PairLock(string key) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
    private Task UserLock(Guid id) => PairLock("user:" + id);
    private IQueryable<Profile> VisiblePeople(bool includeRelatedProfiles = false)
    {
        var q = db.Profiles.Where(p => db.Accounts.Any(a => a.Id == p.Id && !a.Suspended));
        if (Viewer is not { } v) return q.Where(p => p.Public && p.Approved && !p.TestProfile);
        return q.Where(p => (p.Approved || p.Id == v || includeRelatedProfiles && db.Relationships.Any(r => (r.A == v && r.B == p.Id || r.B == v && r.A == p.Id) && (r.State == "pending" || r.State == "accepted")))
            && (!p.TestProfile || p.Id == v || db.Accounts.Any(a => a.Id == v && a.Admin))
            && (db.Accounts.Any(a => a.Id == v && a.Admin) || p.Id == v || !db.Profiles.Any(x => x.Id == v && x.TestProfile) || db.Accounts.Any(a => a.Id == p.Id && a.Admin))
            && !db.Blocks.Any(b => (b.Owner == v && b.Subject == p.Id) || (b.Subject == v && b.Owner == p.Id)));
    }
    private async Task<HashSet<Guid>> VerifiedAccounts()
    {
        var accounts = await db.Accounts.Select(a => new { a.Id, a.Admin, a.Verified, a.Suspended, a.TrustRevoked }).ToDictionaryAsync(a => a.Id);
        var testProfiles = await db.Profiles.Where(p => p.TestProfile).Select(p => p.Id).ToHashSetAsync();
        var parent = await db.Referrals.ToDictionaryAsync(r => r.ReferredId, r => r.ReferrerId);
        var resolved = new Dictionary<Guid, bool>();
        foreach (var start in accounts.Keys)
        {
            if (resolved.ContainsKey(start)) continue;
            var path = new List<Guid>(); var seen = new HashSet<Guid>(); var current = start; var value = false;
            while (seen.Add(current))
            {
                if (resolved.TryGetValue(current, out value)) break;
                if (!accounts.TryGetValue(current, out var account) || account.Suspended || account.TrustRevoked || testProfiles.Contains(current)) { value = false; break; }
                if (account.Verified || account.Admin) { value = true; break; }
                path.Add(current);
                if (!parent.TryGetValue(current, out current)) { value = false; break; }
            }
            foreach (var id in path) resolved[id] = value;
            resolved[start] = value;
        }
        return resolved.Where(x => x.Value).Select(x => x.Key).ToHashSet();
    }
    private async Task<bool> IsVerified(Guid id) => (await VerifiedAccounts()).Contains(id);
    private async Task<bool> CanCreateReferral(Guid id) => await IsVerified(id)
        && await db.Profiles.AnyAsync(p => p.Id == id && p.Approved && !p.TestProfile);
    private static string ReferralHash(string code) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
    private static bool ValidReferralCode(string? code) => code is { Length: 20 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private async Task<Person> PersonDto(Profile p)
    {
        bool dating = Viewer is { } v && await db.Profiles.AnyAsync(x => x.Id == v && x.LookingFor.Contains("Dating"));
        var relation = Viewer is { } id ? await db.Relationships.FindAsync(Pair(id, p.Id)) : null;
        var state = relation?.State == "accepted" ? "connected" : relation?.State == "pending" ? (relation.RequestedBy == Viewer ? "sent" : "received") : "none";
        return new(p.Id, p.Name, p.City, p.Bio, p.Photo, p.Interests,
            p.LookingFor.Where(x => x != "Dating" || dating).ToArray(), p.Instagram, p.LinkedIn, p.Website,
            p.Featured && p.Public && p.FeatureConsent, Viewer.HasValue && await db.Saved.AnyAsync(x => x.UserId == Viewer && x.TargetId == p.Id && x.Kind == "person"),
            p.Id == Viewer ? "self" : state, await db.Accounts.Where(a => a.Id == p.Id).Select(a => a.Demo).SingleAsync(), p.Country, p.State, p.IsVegan, p.VeganVerificationPending);
    }
    private void Notice(Guid user, string kind, string text, string url) => db.Notifications.Add(new() { UserId = user, Kind = kind, Text = text, Url = url });
    public async Task<object> Execute(string method, string path, JsonElement? body)
    {
        var uri = new Uri("https://veganadda.local/" + path.TrimStart('/'));
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        string Q(string key) => q.TryGetValue(key, out var v) ? v.ToString() : "";
        int page = int.TryParse(Q("page"), out var n) ? Math.Clamp(n, 1, 10000) : 1;
        await ActiveViewer();
        if (method == "GET")
        {
            switch (parts[0])
            {
                case "session":
                    var u = Viewer.HasValue ? await User() : null;
                    var ownProfile = u == null ? null : await db.Profiles.SingleOrDefaultAsync(p => p.Id == u.Id);
                    return new Session(u?.Id, u?.Name ?? "Visitor", ownProfile != null, u?.Admin ?? false,
                        env.IsDevelopment() && config.GetValue<bool>("Development:Enabled"),
                        new[] { config["Authentication:Firebase:ProjectId"], config["Authentication:Firebase:ApiKey"], config["Authentication:Firebase:AuthDomain"], config["Authentication:Firebase:AppId"] }.All(x => !string.IsNullOrWhiteSpace(x)), u?.Moderator == true && ownProfile?.IsVegan == true && ownProfile?.VeganVerificationPending != true, ownProfile?.IsVegan, ownProfile?.VeganVerificationPending ?? false);
                case "people":
                    if (parts.Length > 1)
                    {
                        var p = await VisiblePeople(includeRelatedProfiles: true).SingleOrDefaultAsync(p => p.Id == Guid.Parse(parts[1])) ?? throw Missing();
                        return await PersonDto(p);
                    }
                    var people = VisiblePeople();
                    var includeNonVegan = Q("includeNonVegan") == "true";
                    if (includeNonVegan && !Viewer.HasValue) throw new ApiException(401, "Sign in to include non-vegan profiles.");
                    people = includeNonVegan ? people.Where(p => p.IsVegan != null) : people.Where(p => p.IsVegan == true);
                    if (Q("city") is { Length: > 0 } city && city != "All India") people = people.Where(p => p.City == city);
                    // Advanced search and intent filtering require an authenticated profile.
                    if (Viewer.HasValue)
                    {
                        if (Q("search").Trim() is { Length: > 0 } search)
                        { var term = "%" + search.Replace("%", "").Replace("_", "") + "%"; people = people.Where(p => EF.Functions.ILike(p.Name, term) || EF.Functions.ILike(p.Bio, term) || p.Interests.Any(i => EF.Functions.ILike(i, term))); }
                        if (Q("interest") is { Length: > 0 } interest) people = people.Where(p => p.Interests.Contains(interest));
                        if (Q("intent") is { Length: > 0 } intent)
                        {
                            if (!Wire.Intents.Contains(intent)) throw new ApiException(400, "Choose a supported intention.");
                            if (intent == "Dating" && !await db.Profiles.AnyAsync(p => p.Id == Viewer && p.LookingFor.Contains("Dating"))) throw Denied();
                            people = people.Where(p => p.LookingFor.Contains(intent));
                        }
                        if (Guid.TryParse(Q("event"), out var eventId))
                        {
                            if (!await db.Events.AnyAsync(e => e.Id == eventId && e.Published)) throw Missing();
                            people = people.Where(p => db.Attendance.Any(a => a.EventId == eventId && a.UserId == p.Id && a.Visible));
                        }
                    }
                    else if (Q("intent") != "" || Q("event") != "" || Q("search") != "" || Q("interest") != "") throw new ApiException(401, "Sign in to use member filters.");
                    var total = await people.CountAsync();
                    var rows = await people.OrderByDescending(p => p.Featured && p.Public && p.FeatureConsent).ThenBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * 12).Take(12).ToListAsync();
                    var dtos = new List<Person>(); foreach (var p in rows) dtos.Add(await PersonDto(p));
                    return new Page<Person>(dtos, total, page);
                case "profile":
                    var me = await User(); var own = await db.Profiles.FindAsync(me.Id);
                    var canRefer = await CanCreateReferral(me.Id);
                    return own == null ? new ProfileInput { Name = me.Name, CanRefer = canRefer } : new ProfileInput { Name = own.Name, City = own.City, Country = own.Country, State = own.State, Bio = own.Bio, Photo = own.Photo, Interests = own.Interests, LookingFor = own.LookingFor, Instagram = own.Instagram, LinkedIn = own.LinkedIn, Website = own.Website, Public = own.Public, FeatureConsent = own.FeatureConsent, DefaultAttendeeVisible = own.DefaultAttendeeVisible, AdultConfirmed = true, Approved = own.Approved, ReviewNote = own.ReviewNote, CanRefer = canRefer, IsVegan = own.IsVegan, VeganVerificationPending = own.VeganVerificationPending };
                case "connections": return await Connections();
                case "conversations":
                    if (parts.Length == 1) return await Conversations();
                    return await Messages(parts[1], long.TryParse(Q("before"), out var before) ? before : long.MaxValue, long.TryParse(Q("after"), out var after) ? after : null);
                case "events": return await EventQuery(parts.Length > 1 ? Guid.Parse(parts[1]) : null, Q("city"), page);
                case "saved":
                    var saver = await User(true);
                    var personIds = db.Saved.Where(s => s.UserId == saver.Id && s.Kind == "person").Select(s => s.TargetId);
                    var eventIds = db.Saved.Where(s => s.UserId == saver.Id && s.Kind == "event").Select(s => s.TargetId);
                    var savedPeople = new List<Person>(); foreach (var p in await VisiblePeople().Where(p => personIds.Contains(p.Id)).ToListAsync()) savedPeople.Add(await PersonDto(p));
                    var savedEvents = new List<EventCard>(); foreach (var e in await db.Events.Where(e => e.Published && eventIds.Contains(e.Id)).ToListAsync()) savedEvents.Add(await EventDto(e));
                    return new SavedItems(savedPeople, savedEvents);
                case "notifications":
                    var notified = await User();
                    var notifications = db.Notifications.Where(x => x.UserId == notified.Id && x.Kind != "message");
                    var notices = await notifications.OrderByDescending(x => x.Created).Take(50).Select(x => new Notice(x.Id, x.Kind, x.Text, x.Url, x.Read, x.Created)).ToListAsync();
                    return new NotificationList(notices, await notifications.CountAsync(x => !x.Read));
                case "unread": return await Unread();
                case "blocked":
                    var blocker = await User();
                    return await db.Blocks.Where(b => b.Owner == blocker.Id).Join(db.Profiles, b => b.Subject, p => p.Id, (b, p) => new AdminPerson(p.Id, p.Name, false, false, false, false)).ToListAsync();
                case "admin":
                    if (parts.Length == 1) return await Admin(Q("approval"));
                    if (parts.Length == 3 && parts[1] == "members" && Guid.TryParse(parts[2], out var memberId)) return await AdminMemberProfile(memberId);
                    throw Missing();
                case "moderation": return new ModerationData(await ProfileQueue(includeTestProfiles: false), await EventQueue(includeTestEvents: false));
                case "event-submissions": return await MyEventSubmissions();
            }
        }
        else if (method is "POST" or "DELETE")
        {
            switch (parts[0])
            {
                case "profile": return await SaveProfile(Read<ProfileInput>(body));
                case "connections": return await ConnectionAction(parts.Length > 1 ? parts[1] : "request", body);
                case "people": return await PersonAction(Guid.Parse(parts[1]), parts[2], body);
                case "conversations":
                    if (parts[2] == "read") return await MarkRead(parts[1], Read<ReadInput>(body));
                    return await SendMessage(parts[1], Read<MessageInput>(body));
                case "events": return await EventAction(Guid.Parse(parts[1]), parts[2], body);
                case "event-submissions": return await SaveEventSubmission(Read<EventInput>(body));
                case "notifications":
                    return await NotificationAction(method, parts, body);
                case "admin":
                    if (parts[1] == "events") return await SaveEvent(Read<EventInput>(body));
                    if (parts[1] == "profile-review") return await ReviewProfile(Read<ProfileReviewInput>(body));
                    return await Moderate(Read<ModerateInput>(body));
                case "moderation":
                    if (parts.Length > 1 && parts[1] == "profile-review") return await ReviewProfile(Read<ProfileReviewInput>(body), moderator: true);
                    if (parts.Length > 1 && parts[1] == "event-review") return await ReviewEvent(Read<EventReviewInput>(body), moderator: true);
                    throw Missing();
                case "referrals": return await CreateReferralInvite();
                case "account":
                    if (method != "DELETE" || Read<DeleteInput>(body).Confirm != "DELETE") throw new ApiException(400, "Confirm permanent deletion by typing DELETE.");
                    return await DeleteAccount();
            }
        }
        throw Missing();
    }
    public static string Text(string? text, int max, bool required = false)
    {
        text = text?.Trim() ?? "";
        if ((required && text.Length == 0) || text.Length > max || text.Contains('\0')) throw new ApiException(400, $"Please enter {(required ? "1–" : "up to ")}{max} characters.");
        return text;
    }
    public static string? Url(string? value, string? host = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 500 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || (host != null && uri.Host != host && !uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) throw new ApiException(400, "Use a valid HTTPS link for the selected service.");
        return uri.AbsoluteUri;
    }
    public static string Photo(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^/media/[a-f0-9]{32}\.(jpg|png|webp)$")) return value;
        throw new ApiException(400, "Please upload a supported photo.");
    }
    private async Task<object> SaveProfile(ProfileInput input)
    {
        var me = await User();
        if (input.IsVegan is null) throw new ApiException(400, "Choose whether you are vegan or interested in meeting vegans.");
        if (!input.AdultConfirmed) throw new ApiException(400, "You must confirm that you are at least 18 years old.");
        var name = Text(input.Name, 80, true); var city = Text(input.City, 80, true); var bio = Text(input.Bio, 1000);
        var country = Text(input.Country, 80, true); var state = Text(input.State, 80, true);
        if (input.LookingFor == null || input.LookingFor.Any(x => !Wire.Intents.Contains(x))) throw new ApiException(400, "Choose supported intentions.");
        if (input.Interests == null || input.Interests.Length > 10) throw new ApiException(400, "Choose up to 10 interests.");
        var interests = input.Interests.Select(x => Text(x, 40, true)).Distinct().ToArray();
        var instagram = Url(input.Instagram, "instagram.com"); var linkedIn = Url(input.LinkedIn, "linkedin.com"); var website = Url(input.Website); var photo = Photo(input.Photo);
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        var p = await db.Profiles.FindAsync(me.Id);
        var isNewProfile = p == null;
        if (p == null) { p = new Profile { Id = me.Id, AdultConfirmedAt = DateTimeOffset.UtcNow }; db.Profiles.Add(p); }
        var needsVeganVerification = p.IsVegan == false && input.IsVegan == true;
        var reviewChanged = isNewProfile || p.Name != name || p.City != city || p.Country != country || p.State != state || p.Bio != bio || p.Photo != photo
            || !p.Interests.SequenceEqual(interests) || !p.LookingFor.SequenceEqual(input.LookingFor.Distinct())
            || p.Instagram != instagram || p.LinkedIn != linkedIn || p.Website != website || p.Public != input.Public || p.IsVegan != input.IsVegan;
        if (!string.IsNullOrWhiteSpace(input.ReferralCode) && !await db.Referrals.AnyAsync(r => r.ReferredId == me.Id))
        {
            if (!ValidReferralCode(input.ReferralCode)) throw new ApiException(400, "This referral link is invalid or expired.");
            var inviteHash = ReferralHash(input.ReferralCode!);
            await PairLock("referral-invite:" + inviteHash);
            var invite = await db.ReferralInvites.SingleOrDefaultAsync(i => i.TokenHash == inviteHash);
            if (invite == null || invite.Revoked || invite.ReferredId.HasValue || invite.Expires <= DateTimeOffset.UtcNow || invite.ReferrerId == me.Id || !await IsVerified(invite.ReferrerId) || !await db.Profiles.AnyAsync(p => p.Id == invite.ReferrerId && p.Approved && !p.TestProfile))
                throw new ApiException(400, "This referral link is invalid or expired.");
            var parent = await db.Referrals.ToDictionaryAsync(r => r.ReferredId, r => r.ReferrerId);
            var ancestor = invite.ReferrerId;
            var visited = new HashSet<Guid>();
            while (visited.Add(ancestor))
            {
                if (ancestor == me.Id) throw new ApiException(400, "This referral link is invalid or expired.");
                if (!parent.TryGetValue(ancestor, out ancestor)) break;
            }
            invite.ReferredId = me.Id;
            db.Referrals.Add(new() { ReferredId = me.Id, ReferrerId = invite.ReferrerId });
        }
        p.Name = name; p.City = city; p.Country = country; p.State = state; p.Bio = bio; p.LookingFor = input.LookingFor.Distinct().ToArray(); p.Interests = interests;
        p.Instagram = instagram; p.LinkedIn = linkedIn; p.Website = website;
        // A photo must belong to this account (or be its existing development fixture).
        if (photo != "" && photo != p.Photo && !System.IO.File.Exists(System.IO.Path.Combine(config["Storage:MediaPath"] ?? "uploads", photo[7..] + ".owner-" + me.Id.ToString("N")))) throw Denied();
        p.Photo = photo; p.Public = input.Public; p.FeatureConsent = input.FeatureConsent; p.DefaultAttendeeVisible = input.DefaultAttendeeVisible; p.IsVegan = input.IsVegan;
        if (needsVeganVerification) p.VeganVerificationPending = true;
        if (input.IsVegan == false) { p.VeganVerificationPending = false; me.Moderator = false; }
        if (reviewChanged) { p.Approved = false; p.ReviewNote = null; p.ReviewedAt = null; p.ReviewedBy = null; }
        if (!p.Public || !p.FeatureConsent) p.Featured = false;
        me.Name = name;
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new Ok();
    }
    private async Task<object> CreateReferralInvite()
    {
        var me = await User(true);
        if (!await CanCreateReferral(me.Id)) throw new ApiException(403, "Referrals require a verified account and an approved community profile.");
        var now = DateTimeOffset.UtcNow;
        if (await db.ReferralInvites.CountAsync(i => i.ReferrerId == me.Id && !i.Revoked && !i.ReferredId.HasValue && i.Expires > now) >= 5)
            throw new ApiException(429, "Use or let an earlier referral link expire before creating another.");
        var raw = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(15)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = now.AddDays(30);
        db.ReferralInvites.Add(new() { TokenHash = ReferralHash(raw), ReferrerId = me.Id, Expires = expires });
        await db.SaveChangesAsync();
        return new ReferralInviteResult(raw, expires);
    }
}
