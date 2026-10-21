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
        var me = await User();
        if (input.IsVegan is null) throw new ApiException(400, "Choose whether you are vegan or interested in meeting vegans.");
        if (!input.AdultConfirmed) throw new ApiException(400, "You must confirm that you are at least 18 years old.");
        var birthDate = ReadBirthDetails(input);
        var name = Text(input.Name, 80, true); var city = Text(input.City, 80, true); var bio = Text(input.Bio, 1000);
        var country = Text(input.Country, 80, true); var state = Text(input.State, 80, true);
        if (input.SavePreciseLocation && (input.Latitude is not double latitude || !double.IsFinite(latitude) || latitude < -90 || latitude > 90
            || input.Longitude is not double longitude || !double.IsFinite(longitude) || longitude < -180 || longitude > 180))
            throw new ApiException(400, "Use my location to capture a valid precise location, or turn off saving precise location.");
        if (input.LookingFor == null || input.LookingFor.Any(x => !Wire.Intents.Contains(x))) throw new ApiException(400, "Choose supported intentions.");
        if (input.Interests == null || input.Interests.Length > 10) throw new ApiException(400, "Choose up to 10 interests.");
        var interests = input.Interests.Select(x => Text(x, 40, true)).Distinct().ToArray();
        var instagram = Url(input.Instagram, "instagram.com"); var linkedIn = Url(input.LinkedIn, "linkedin.com"); var website = Url(input.Website); var photo = Photo(input.Photo);
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        var p = await db.Profiles.FindAsync(me.Id);
        var isNewProfile = p == null;
        if (p == null) { p = new Profile { Id = me.Id, AdultConfirmedAt = DateTimeOffset.UtcNow }; db.Profiles.Add(p); }
        var needsVeganVerification = p.IsVegan == false && input.IsVegan == true;
        var wasApproved = p.Approved;
        var reviewChanged = isNewProfile || p.BirthDate != birthDate || p.Name != name || p.City != city || p.Country != country || p.State != state || p.Bio != bio || p.Photo != photo
            || !p.Interests.SequenceEqual(interests) || !p.LookingFor.SequenceEqual(input.LookingFor.Distinct())
            || p.Instagram != instagram || p.LinkedIn != linkedIn || p.Website != website || p.Public != input.Public || p.IsVegan != input.IsVegan;
        if (!string.IsNullOrWhiteSpace(input.ReferralCode) && !await db.Referrals.AnyAsync(r => r.ReferredId == me.Id))
        {
            if (!ReferralTrust.ValidReferralCode(input.ReferralCode)) throw new ApiException(400, "This referral link is invalid or expired.");
            var inviteHash = ReferralTrust.ReferralHash(input.ReferralCode!);
            await PairLock("referral-invite:" + inviteHash);
            var invite = await db.ReferralInvites.SingleOrDefaultAsync(i => i.TokenHash == inviteHash);
            if (invite == null || invite.Revoked || invite.ReferredId.HasValue || invite.Expires <= DateTimeOffset.UtcNow || invite.ReferrerId == me.Id || !await trust.IsVerified(invite.ReferrerId) || !await db.Profiles.AnyAsync(p => p.Id == invite.ReferrerId && p.Approved && !p.TestProfile))
                throw new ApiException(400, "This referral link is invalid or expired.");
            if ((await trust.Ancestors(invite.ReferrerId)).Contains(me.Id))
                throw new ApiException(400, "This referral link is invalid or expired.");
            invite.ReferredId = me.Id;
            db.Referrals.Add(new() { ReferredId = me.Id, ReferrerId = invite.ReferrerId });
        }
        p.Name = name; p.City = city; p.Country = country; p.State = state; p.Bio = bio; p.LookingFor = input.LookingFor.Distinct().ToArray(); p.Interests = interests;
        p.BirthDate = birthDate;
        p.HasExactBirthDate = input.BirthDay.HasValue;
        p.Latitude = input.SavePreciseLocation ? input.Latitude : null;
        p.Longitude = input.SavePreciseLocation ? input.Longitude : null;
        p.Instagram = instagram; p.LinkedIn = linkedIn; p.Website = website;
        // A photo must belong to this account (or be its existing development fixture).
        if (photo != "" && photo != p.Photo && !System.IO.File.Exists(System.IO.Path.Combine(config["Storage:MediaPath"] ?? "uploads", photo[7..] + ".owner-" + me.Id.ToString("N")))) throw Denied();
        p.Photo = photo; p.Public = input.Public; p.FeatureConsent = input.FeatureConsent; p.DefaultAttendeeVisible = input.DefaultAttendeeVisible; p.IsVegan = input.IsVegan;
        if (needsVeganVerification) p.VeganVerificationPending = true;
        if (input.IsVegan == false) { p.VeganVerificationPending = false; me.Moderator = false; }
        if (reviewChanged) { p.Approved = false; p.ReviewNote = null; p.ReviewedAt = null; p.ReviewedBy = null; }
        if (!p.Public || !p.FeatureConsent) p.Featured = false;
        me.Name = name;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        lifecycle.Record(isNewProfile ? LifecycleEvent.ProfileCreated : wasApproved && reviewChanged ? LifecycleEvent.ProfileResubmitted : LifecycleEvent.ProfileUpdated, me.Id);
        return new Ok();
    }
    public async Task<Ok> DeleteAccount()
    {
        var me = await User();
        if (me.Admin) throw new ApiException(409, "Transfer administrator access before deleting this account.");
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        var peers = await db.Relationships.Where(r => r.A == me.Id || r.B == me.Id).Select(r => r.A == me.Id ? r.B : r.A).ToArrayAsync();
        await db.Saved.Where(s => s.TargetId == me.Id && s.Kind == "person").ExecuteDeleteAsync();
        // Cascade removes all conversations and both participants' messages in those conversations.
        db.Accounts.Remove(me); await db.SaveChangesAsync(); await tx.CommitAsync();
        lifecycle.Record(LifecycleEvent.AccountDeleted, me.Id);
        var media = config["Storage:MediaPath"] ?? "uploads";
        if (Directory.Exists(media)) foreach (var marker in Directory.EnumerateFiles(media, "*.owner-" + me.Id.ToString("N")))
        { File.Delete(marker[..marker.LastIndexOf(".owner-", StringComparison.Ordinal)]); File.Delete(marker); }
        await changes.Changed(peers); return new Ok();
    }
    public async Task<ProfileInput> GetProfile()
    {
        var me = await User(); var own = await db.Profiles.FindAsync(me.Id);
                    var canRefer = await trust.CanCreateReferral(me.Id);
                    return own == null ? new ProfileInput { Name = me.Name, CanRefer = canRefer } : new ProfileInput { Name = own.Name, BirthYear = own.BirthDate?.Year, BirthMonth = own.BirthDate?.Month, BirthDay = own.HasExactBirthDate ? own.BirthDate?.Day : null, City = own.City, Country = own.Country, State = own.State, SavePreciseLocation = own.Latitude.HasValue && own.Longitude.HasValue, Latitude = own.Latitude, Longitude = own.Longitude, Bio = own.Bio, Photo = own.Photo, Interests = own.Interests, LookingFor = own.LookingFor, Instagram = own.Instagram, LinkedIn = own.LinkedIn, Website = own.Website, Public = own.Public, FeatureConsent = own.FeatureConsent, DefaultAttendeeVisible = own.DefaultAttendeeVisible, AdultConfirmed = true, Approved = own.Approved, ReviewNote = own.ReviewNote, CanRefer = canRefer, IsVegan = own.IsVegan, VeganVerificationPending = own.VeganVerificationPending };
    }
    public async Task<Person> GetPerson(Guid id)
    {
        await ActiveViewer();
        var person = await visibility.Query(true).SingleOrDefaultAsync(p => p.Id == id) ?? throw Missing();
        return await people.One(person);
    }
}
