using VeganAdda.Contracts;
using VeganAdda.Web.Features.Verification;
using VeganAdda.Web.Services;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Referrals;
using VeganAdda.Web.Features.Events;
using VeganAdda.Web.Features.Discovery;
using VeganAdda.Web.Features.Connections;
using VeganAdda.Web.Features.Messaging;
using VeganAdda.Web.Features.Moderation;
using VeganAdda.Web.Features.Notifications;
using VeganAdda.Web.Features.Accounts;
using VeganAdda.Web.Features.Saved;
using VeganAdda.Web.Observability;

namespace VeganAdda.Web.Features;

public static class FeatureRegistration
{
    public static void MapCommunityFeatures(this WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<FeatureRequestFilter>();
        VerificationEndpoints.Map(api);
        AccountsEndpoints.Map(api);
        ProfilesEndpoints.Map(api);
        DiscoveryEndpoints.Map(api);
        ConnectionsEndpoints.Map(api);
        MessagingEndpoints.Map(api);
        EventsEndpoints.Map(api);
        ModerationEndpoints.Map(api);
        NotificationsEndpoints.Map(api);
        ReferralsEndpoints.Map(api);
        SavedEndpoints.Map(api);
    }

    public static IServiceCollection AddCommunityFeatures(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<LifecycleEvents>();
        services.AddScoped<VerificationService>();
        services.AddScoped<FeatureContext>();
        services.AddScoped<CommunityService>();
        services.AddScoped<InboxService>();
        services.AddScoped<ProfileVisibility>();
        services.AddScoped<PeopleProjection>();
        services.AddScoped<ReferralTrust>();
        services.AddScoped<EventProjection>();
        services.AddScoped<SessionService>();
        services.AddScoped<SavedService>();
        services.AddScoped<ProfilesService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<ConnectionsService>();
        services.AddScoped<MessagingService>();
        services.AddScoped<EventsService>();
        services.AddScoped<ModerationService>();
        services.AddScoped<NotificationsService>();
        services.AddScoped<ReferralsService>();
        return services;
    }
}
