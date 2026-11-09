using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Accounts;

public sealed class SessionService(FeatureContext context) : FeatureService(context)
{
    public async Task<Session> GetSession()
    {
        var u = Viewer.HasValue ? await User(allowIncomplete: true) : null;
        var ownProfile = u == null ? null : await db.Profiles.SingleOrDefaultAsync(p => p.Id == u.Id);
        var complete = BasicProfile.IsComplete(ownProfile);
        return new Session(u?.Id, u?.Name ?? "Visitor", ownProfile != null, u?.Admin ?? false,
            env.IsDevelopment() && config.GetValue<bool>("Development:Enabled"),
            new[] { config["Authentication:Firebase:ProjectId"], config["Authentication:Firebase:ApiKey"], config["Authentication:Firebase:AuthDomain"], config["Authentication:Firebase:AppId"] }.All(x => !string.IsNullOrWhiteSpace(x)),
            u?.Moderator == true && ownProfile?.IsVegan == true && ownProfile?.VeganVerified == true && ownProfile?.TestProfile != true && complete,
            ownProfile?.IsVegan, ownProfile?.VeganVerified ?? false, complete)
        {
            Announcements = complete ? GetAnnouncements(!string.IsNullOrWhiteSpace(ownProfile?.Photo)) : []
        };
    }

    private Announcement[] GetAnnouncements(bool hasProfilePhoto)
        => config.GetSection("FeatureAnnouncements").GetChildren()
            .Select(settings => ReadAnnouncement(settings, hasProfilePhoto))
            .OfType<Announcement>()
            .DistinctBy(announcement => announcement.Id)
            .ToArray();

    private static Announcement? ReadAnnouncement(IConfigurationSection settings, bool hasProfilePhoto)
    {
        if (!hasProfilePhoto && bool.TryParse(settings["RequiresProfilePhoto"], out var requiresPhoto) && requiresPhoto)
            return null;

        var id = settings["Id"]?.Trim();
        var title = settings["Title"]?.Trim();
        var message = settings["Message"]?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(message))
            return null;

        var path = settings["ActionPath"]?.Trim();
        var label = settings["ActionLabel"]?.Trim();
        return !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(label) && ProfileSetup.SafeReturnUrl(path) == path
            ? new Announcement(id, title, message, label, path)
            : new Announcement(id, title, message);
    }
}
