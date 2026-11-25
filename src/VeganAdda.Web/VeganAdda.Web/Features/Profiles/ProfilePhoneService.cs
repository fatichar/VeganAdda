using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Features.Profiles;

public sealed class ProfilePhoneService(FeatureContext context, IFirebaseIdentityVerifier firebase) : FeatureService(context)
{
    private string ProjectId => config["Authentication:Firebase:ProjectId"] ?? "";
    private bool CanVerify(Account account) => ProjectId.Length > 0
        && config.GetValue<bool>("Authentication:Firebase:ProfilePhoneVerificationEnabled")
        && !account.Demo && account.Subject.StartsWith($"firebase:{ProjectId}:", StringComparison.Ordinal);

    public async Task<ProfilePhone> GetPhone()
    {
        var me = await User();
        var phone = await db.ProfilePhones.FindAsync(me.Id);
        return Result(phone, await CanVerifyProfile(me));
    }

    public async Task<ProfilePhoneAuthorization> Authorize(FirebaseLoginInput proof)
    {
        var me = await User();
        return new((await VerifyOwner(me, proof.IdToken)).Uid);
    }

    public async Task<ProfilePhone> Verify(ProfilePhoneProof proof)
    {
        var me = await User();
        await VerifyOwner(me, proof.GoogleIdToken);
        if (proof.PhoneIdToken is not { Length: > 0 and <= 12000 } phoneToken) throw new ApiException(400, "Request a new verification code.");
        var identity = await firebase.Verify(phoneToken);
        var number = identity?.PhoneNumber;
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        if (identity?.Provider != "phone" || identity.AuthenticatedAt < cutoff
            || number is not { Length: >= 8 and <= 16 } || number[0] != '+' || number[1] == '0'
            || number.Skip(1).Any(c => !char.IsAsciiDigit(c)) || await firebase.GetCurrentPhoneNumber(identity.Uid) != number)
            throw new ApiException(400, "Verify the number by SMS before adding it to your profile.");

        await using var tx = await db.Database.BeginTransactionAsync();
        await PairLock("profile-phone:" + number);
        var previousOwner = await db.ProfilePhones.Where(p => p.ProfileId != me.Id && p.Number == number)
            .Select(p => (Guid?)p.ProfileId).SingleOrDefaultAsync();
        var owners = previousOwner is { } id ? new[] { me.Id, id } : [me.Id];
        foreach (var owner in owners.Order()) await UserLock(owner);
        if (!await db.Accounts.AnyAsync(a => a.Id == me.Id && !a.Suspended)) throw Denied();
        if (!await CanVerifyProfile(me)) throw Denied();
        var verificationSubject = $"firebase:{ProjectId}:{identity.Uid}";
        var canDeleteVerificationUser = !await db.Accounts.AnyAsync(a => a.Subject == verificationSubject);
        // A number can be reassigned. Only the current verified holder can continue sharing it.
        await db.ProfilePhones.Where(p => p.ProfileId != me.Id && p.Number == number).ExecuteDeleteAsync();
        var phone = await db.ProfilePhones.FindAsync(me.Id);
        if (phone == null)
        {
            phone = new VerifiedProfilePhone { ProfileId = me.Id, Number = number };
            db.ProfilePhones.Add(phone);
        }
        else if (phone.Number != number)
        {
            phone.Shared = false;
            phone.Number = number;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        if (canDeleteVerificationUser) await firebase.TryDeletePhoneVerificationUser(identity);
        return Result(phone, true);
    }

    public Task<ProfilePhone> Share(ProfilePhoneVisibility input) => Update(phone =>
    {
        if (input.Shared && (phone == null || input.Number != phone.Number))
            throw new ApiException(409, "The verified phone number changed. Reload your profile before sharing it.");
        if (phone != null) phone.Shared = input.Shared;
        return phone;
    });

    public Task<ProfilePhone> Remove() => Update(phone =>
    {
        if (phone != null) db.ProfilePhones.Remove(phone);
        return null;
    });

    private async Task<ProfilePhone> Update(Func<VerifiedProfilePhone?, VerifiedProfilePhone?> change)
    {
        var me = await User();
        await using var tx = await db.Database.BeginTransactionAsync();
        await UserLock(me.Id);
        var phone = change(await db.ProfilePhones.FindAsync(me.Id));
        var canVerify = await CanVerifyProfile(me);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result(phone, canVerify);
    }

    private async Task<VerifiedFirebaseIdentity> VerifyOwner(Account me, string? idToken)
    {
        if (!await CanVerifyProfile(me)) throw Denied();
        if (idToken is not { Length: > 0 and <= 12000 } token) throw new ApiException(400, "Sign in with Google again to verify your number.");
        var identity = await firebase.Verify(token);
        if (identity?.Provider != "google.com" || me.Subject != $"firebase:{ProjectId}:{identity.Uid}")
            throw Denied();
        return identity;
    }

    private async Task<bool> CanVerifyProfile(Account me)
        => CanVerify(me) && await db.Profiles.AnyAsync(p => p.Id == me.Id && !p.TestProfile);

    private static ProfilePhone Result(VerifiedProfilePhone? phone, bool canVerify)
        => new(phone?.Number, phone?.Shared ?? false, canVerify);
}
