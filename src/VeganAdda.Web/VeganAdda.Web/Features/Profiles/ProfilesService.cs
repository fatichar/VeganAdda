using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Referrals;
using VeganAdda.Web.Observability;
using static VeganAdda.Web.Features.Profiles.BirthDetails;

namespace VeganAdda.Web.Features.Profiles;

public sealed class ProfilesService(FeatureContext context, ProfileVisibility visibility, PeopleProjection people, ReferralTrust trust, LifecycleEvents lifecycle) : FeatureService(context)
{
    public async Task<Ok> SaveProfile(ProfileInput input)
    {
        var me = await User(allowIncomplete: true);
        if (input.IsVegan is null) throw new ApiException(400, "Choose whether you are vegan or interested in meeting vegans.");
        var birthDate = ReadBirthDetails(input);
        var gender = Text(input.Gender, 40, true);
        if (!Wire.Genders.Contains(gender)) throw new ApiException(400, "Choose a supported gender.");
        var name = ValidateName(input.Name);
        var city = Text(input.City, 80, true); var bio = Text(input.Bio, 1000); var profession = Text(input.Profession, 80);
        var country = Text(input.Country, 80, true); var state = Text(input.State, 80, true);
        if (input.SavePreciseLocation && (input.Latitude is not double latitude || !double.IsFinite(latitude) || latitude < -90 || latitude > 90
            || input.Longitude is not double longitude || !double.IsFinite(longitude) || longitude < -180 || longitude > 180))
            throw new ApiException(400, "Use my location to capture a valid precise location, or turn off saving precise location.");
        if (input.LookingFor == null || input.LookingFor.Any(x => !Wire.Intents.Contains(x))) throw new ApiException(400, "Choose supported intentions.");
        if (input.Interests == null || input.Interests.Length > 10) throw new ApiException(400, "Choose up to 10 interests.");
        var interests = input.Interests.Select(x => Text(x, 40, true)).Distinct().ToArray();
        var instagram = Url(input.Instagram, "instagram.com"); var linkedIn = Url(input.LinkedIn, "linkedin.com"); var website = Url(input.Website); var photo = Photo(input.Photo);
        if (input.PhotoFocus is { } focus && (focus.X is < 0 or > 100 || focus.Y is < 0 or > 100))
            throw new ApiException(400, "Choose a photo position within the image.");
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        // Queried before tracked changes are saved, so this reflects the stored profile.
        var wasAwaitingReview = await VerificationReview.IsAwaiting(db, me.Id);
        var p = await db.Profiles.FindAsync(me.Id);
        var isNewProfile = p == null;
        if (p == null) { p = new Profile { Id = me.Id, AdultConfirmedAt = DateTimeOffset.UtcNow }; db.Profiles.Add(p); }
        if (!string.IsNullOrWhiteSpace(input.ReferralCode))
            throw new ApiException(410, "Referral codes have been replaced. Request vegan verification from your profile.");
        p.Name = name; p.City = city; p.Country = country; p.State = state; p.Bio = bio; p.Profession = profession; p.LookingFor = input.LookingFor.Distinct().ToArray(); p.Interests = interests;
        p.BirthDate = birthDate;
        p.Gender = gender;
        p.HasExactBirthDate = input.BirthDay.HasValue;
        var center = input.SavePreciseLocation ? null : CityCenterCatalog.Find(country, state, city);
        p.HasPreciseLocation = input.SavePreciseLocation;
        p.Latitude = input.SavePreciseLocation ? input.Latitude : center?.Latitude;
        p.Longitude = input.SavePreciseLocation ? input.Longitude : center?.Longitude;
        p.Instagram = instagram; p.LinkedIn = linkedIn; p.Website = website;
        // A photo must belong to this account (or be its existing development fixture).
        if (photo != "" && photo != p.Photo && !System.IO.File.Exists(System.IO.Path.Combine(config["Storage:MediaPath"] ?? "uploads", photo[7..] + ".owner-" + me.Id.ToString("N")))) throw Denied();
        if (photo == "" || photo != p.Photo || input.PhotoFocus != null)
        {
            p.PhotoFocusX = photo == "" ? null : input.PhotoFocus?.X;
            p.PhotoFocusY = photo == "" ? null : input.PhotoFocus?.Y;
        }
        p.Photo = photo; p.Public = input.Public; p.FeatureConsent = true; p.DefaultAttendeeVisible = input.DefaultAttendeeVisible; p.IsVegan = input.IsVegan;
        if (input.IsVegan == false)
        {
            p.VeganVerified = false; me.Moderator = false;
            await db.VerificationRequests.Where(r => r.ApplicantId == me.Id && r.Status == VerificationState.UnderReview)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, VerificationState.Withdrawn).SetProperty(r => r.Resolved, DateTimeOffset.UtcNow));
        }
        if (!p.Public || !p.FeatureConsent) p.Featured = false;
        if (p.AdultConfirmedAt == default) p.AdultConfirmedAt = DateTimeOffset.UtcNow;
        me.Name = name;
        await db.Notifications.Where(n => n.UserId == me.Id && n.Kind == "profile-gender-required" && !n.Read)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.Read, true));
        await db.SaveChangesAsync();
        // Adding Instagram or LinkedIn can make a pending referral-only request reviewable.
        List<Guid> reviewers = [];
        if (!wasAwaitingReview && await VerificationReview.IsAwaiting(db, me.Id))
        {
            reviewers = await VerificationReview.NotifyReviewers(db, "A member requested vegan verification.");
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        lifecycle.Record(isNewProfile ? LifecycleEvent.ProfileCreated : LifecycleEvent.ProfileUpdated, me.Id);
        await changes.Changed(ChangeArea.Profiles | ChangeArea.Notifications, [.. reviewers.Append(me.Id).Distinct()]);
        return new Ok();
    }
    public async Task<Ok> DeleteAccount()
    {
        var me = await User(allowIncomplete: true);
        return await DeleteAccountData(me.Id, me.Id, requireProfile: false, CancellationToken.None);
    }

    public async Task<Ok> DeleteMemberProfile(Guid id, DeleteInput input, CancellationToken cancellationToken = default)
    {
        var admin = await User(admin: true);
        if (input.Confirm != "DELETE") throw new ApiException(400, "Confirm permanent deletion by typing DELETE.");
        return await DeleteAccountData(id, admin.Id, requireProfile: true, cancellationToken);
    }

    private async Task<Ok> DeleteAccountData(Guid id, Guid actorId, bool requireProfile, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await UserLock(id);
        var target = db.Accounts.Where(a => a.Id == id);
        if (requireProfile && !await db.Profiles.AnyAsync(p => p.Id == id, cancellationToken)) throw Missing();
        var peers = await db.Relationships.Where(r => r.A == id || r.B == id)
            .Select(r => r.A == id ? r.B : r.A).ToArrayAsync(cancellationToken);
        await db.Saved.Where(s => s.TargetId == id && s.Kind == "person").ExecuteDeleteAsync(cancellationToken);
        // Cascade removes all conversations and both participants' messages in those conversations.
        if (await target.Where(a => !a.Admin).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            if (await target.AnyAsync(cancellationToken))
                throw new ApiException(409, "Transfer administrator access before deleting this account.");
            throw Missing();
        }
        await tx.CommitAsync(cancellationToken);
        lifecycle.Record(LifecycleEvent.AccountDeleted, id, actorId);
        var media = config["Storage:MediaPath"] ?? "uploads";
        if (Directory.Exists(media)) foreach (var marker in Directory.EnumerateFiles(media, "*.owner-" + id.ToString("N")))
        {
            var original = marker[..marker.LastIndexOf(".owner-", StringComparison.Ordinal)];
            File.Delete(original); File.Delete(Path.Combine(media, "previews", Path.GetFileName(original))); File.Delete(marker);
        }
        await changes.Changed([.. peers, id, actorId]);
        return new Ok();
    }
    public async Task<ProfileInput> GetProfile()
    {
        var me = await User(allowIncomplete: true); var own = await db.Profiles.FindAsync(me.Id);
                    var canRefer = await trust.CanCreateReferral(me.Id);
                    return own == null ? new ProfileInput { Name = me.Name, CanRefer = canRefer } : new ProfileInput { Name = own.Name, BirthYear = own.BirthDate?.Year, BirthMonth = own.BirthDate?.Month, BirthDay = own.HasExactBirthDate ? own.BirthDate?.Day : null, City = own.City, Country = own.Country, State = own.State, SavePreciseLocation = own.HasPreciseLocation, Latitude = own.HasPreciseLocation ? own.Latitude : null, Longitude = own.HasPreciseLocation ? own.Longitude : null, Bio = own.Bio, Profession = own.Profession, Gender = own.Gender, Photo = own.Photo, PhotoFocus = PeopleProjection.GetPhotoFocus(own), Interests = own.Interests, LookingFor = own.LookingFor, Instagram = own.Instagram, LinkedIn = own.LinkedIn, Website = own.Website, Public = own.Public, DefaultAttendeeVisible = own.DefaultAttendeeVisible, VeganVerified = own.VeganVerified, ReviewNote = own.ReviewNote, CanRefer = canRefer, IsVegan = own.IsVegan, HasReferrer = await db.Referrals.AnyAsync(r => r.ReferredId == me.Id) };
    }
    public async Task<Person> GetPerson(Guid id)
    {
        await ActiveViewer();
        if (!Viewer.HasValue) throw new ApiException(401, "Sign in to view member profiles.");
        var person = await visibility.Query(true).SingleOrDefaultAsync(p => p.Id == id) ?? throw Missing();
        return await people.One(person);
    }

    public static string ValidateName(string? value)
    {
        var name = Text(value, 80, true);
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^(?=.*\p{L})[\p{L}\p{M}\p{Zs}.'’,-]+$"))
            throw new ApiException(400, "Enter a name using letters, spaces, apostrophes, hyphens, periods or commas.");
        return name;
    }

}
