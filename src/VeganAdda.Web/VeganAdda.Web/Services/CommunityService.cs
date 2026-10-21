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
using VeganAdda.Web.Features.Discovery;
using VeganAdda.Web.Features.Connections;
using VeganAdda.Web.Features.Messaging;
using VeganAdda.Web.Features.Moderation;
using VeganAdda.Web.Features.Notifications;
using VeganAdda.Web.Features.Accounts;
using VeganAdda.Web.Features.Saved;

namespace VeganAdda.Web.Services;

// Compatibility adapter for existing path-based clients. New endpoints use feature services.
public sealed class CommunityService(FeatureContext context,
    ProfilesService profiles,
    DiscoveryService discovery,
    ConnectionsService connections,
    MessagingService messaging,
    EventsService events,
    ModerationService moderation,
    NotificationsService notifications,
    ReferralsService referrals,
    SessionService session,
    SavedService saved, InboxService inbox) : FeatureService(context)
{
    public async Task<object> Execute(string method, string path, JsonElement? body)
    {
        var uri = new Uri("https://veganadda.local/" + path.TrimStart('/'));
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        string Q(string key) => q.TryGetValue(key, out var v) ? v.ToString() : "";
        int page = int.TryParse(Q("page"), out var n) ? Math.Clamp(n, 1, 10000) : 1;
        if (Viewer.HasValue) await User(allowIncomplete: true);
        if (method == "GET")
        {
            switch (parts[0])
            {
                case "session": return await session.GetSession();
                case "people":
                    if (parts.Length > 1)
                    {
                        return await profiles.GetPerson(Guid.Parse(parts[1]));
                    }
                    return await discovery.SearchPeople(new DiscoveryInput { Event = Q("event"), CheckedIn = Q("checkedIn") == "true", Country = Q("country"), State = Q("state"), City = Q("city"), Search = Q("search"), Genders = Q("gender").Split(',', StringSplitOptions.RemoveEmptyEntries), Interest = Q("interest"), Intent = Q("intent"), VerifiedVegansOnly = Q("verifiedVegansOnly") == "true", IncludeNonVegan = Q("includeNonVegan") == "true", MinAge = ParseAge(Q("minAge")), MaxAge = ParseAge(Q("maxAge")), Page = page });
                case "profile": return await profiles.GetProfile();
                case "connections": return await connections.Connections();
                case "conversations":
                    if (parts.Length == 1) return await messaging.Conversations();
                    return await messaging.Messages(parts[1], long.TryParse(Q("before"), out var before) ? before : long.MaxValue, long.TryParse(Q("after"), out var after) ? after : null);
                case "events": return parts.Length > 1 ? await events.GetEvent(Guid.Parse(parts[1])) : await events.ListEvents(Q("city"), page, Q("includePast") == "true", Q("country"), Q("state"));
                case "saved": return await saved.GetSaved(EndpointSupport.PageFor(Q("peoplePage")), EndpointSupport.PageFor(Q("eventsPage")));
                case "inbox": return await inbox.GetInbox(EndpointSupport.PageFor(Q("chatPage")), EndpointSupport.PageFor(Q("requestPage")), Q("key") is { Length: > 0 } key ? key : null);
                case "notifications": return await notifications.GetNotifications(page);
                case "unread": return await notifications.Unread();
                case "blocked": return await connections.GetBlocked();
                case "admin":
                    if (parts.Length == 1) return await moderation.Admin(Q("verification"), Q("section") is { Length: > 0 } section ? section : "Members", page);
                    if (parts.Length == 3 && parts[1] == "members" && Guid.TryParse(parts[2], out var memberId)) return await moderation.AdminMemberProfile(memberId);
                    throw Missing();
                case "moderation": return await moderation.Moderation(Q("section") is { Length: > 0 } moderationSection ? moderationSection : "Profiles", page);
                case "event-submissions": return await events.MyEventSubmissions();
            }
        }
        else if (method is "POST" or "DELETE")
        {
            switch (parts[0])
            {
                case "profile": return await profiles.SaveProfile(Read<ProfileInput>(body));
                case "connections": return await connections.ConnectionAction(parts.Length > 1 ? parts[1] : "request", body);
                case "people":
                    if (method == "POST" && parts.Length == 2 && parts[1] == "search") return await discovery.SearchPeople(Read<DiscoveryInput>(body));
                    return await connections.PersonAction(Guid.Parse(parts[1]), parts[2], body);
                case "conversations":
                    if (parts[2] == "read") return await messaging.MarkRead(parts[1], Read<ReadInput>(body));
                    return await messaging.SendMessage(parts[1], Read<MessageInput>(body));
                case "events": return await events.EventAction(Guid.Parse(parts[1]), parts[2], body);
                case "event-submissions": return await events.SaveEventSubmission(Read<EventInput>(body));
                case "notifications":
                    return await notifications.NotificationAction(method, parts, body);
                case "admin":
                    if (parts[1] == "events") return await events.SaveEvent(Read<EventInput>(body));
                    if (parts[1] == "profile-review") return await moderation.ReviewProfile(Read<VeganVerificationInput>(body));
                    return await moderation.Moderate(Read<ModerateInput>(body));
                case "moderation":
                    if (parts.Length > 1 && parts[1] == "profile-review") return await moderation.ReviewProfile(Read<VeganVerificationInput>(body), moderator: true);
                    if (parts.Length > 1 && parts[1] == "event-review") return await events.ReviewEvent(Read<EventReviewInput>(body), moderator: true);
                    throw Missing();
                case "referrals": return await referrals.CreateReferralInvite();
                case "account":
                    if (method != "DELETE" || Read<DeleteInput>(body).Confirm != "DELETE") throw new ApiException(400, "Confirm permanent deletion by typing DELETE.");
                    return await profiles.DeleteAccount();
            }
        }
        throw Missing();
    }
}
